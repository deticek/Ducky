using System;
using System.Text;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Media;

namespace ducky;

public sealed class VtTerminalBuffer
{
    private const int MaxCellChangeEvents = 400;
    private const int MaxScrollbackRows = 5000;

    public readonly record struct Cell(
        string Character,
        int Width,
        Color Foreground,
        Color Background)
    {
        public Cell(char character, Color foreground, Color background)
            : this(character.ToString(), 1, foreground, background)
        {
        }
    }
    private char pendingHighSurrogate;

    private int columns;
    private int rows;
    private Cell[,] cells;

    private Cell[,]? savedMainScreen;
    private int mainScreenX;
    private int mainScreenY;

    private int x;
    private int y;
    private int savedX;
    private int savedY;

    private int scrollTop;
    private int scrollBottom;
    private readonly List<Cell[]> scrollback = new();
    private double scrollbackOffset;
    private double savedMainScrollbackOffset;

    private bool alternateScreen;
    private bool wrapPending;
    private bool originMode;
    private bool autoWrap = true;
    private bool bracketedPasteEnabled;
    private string lastCsiCommand = "input";
    private readonly Queue<string> recentCellChanges = new();

    private Color foreground;
    private Color background;

    private Color defaultForeground;
    private Color defaultBackground;

    public bool CursorVisible { get; private set; } = true;
    public bool BracketedPasteEnabled => bracketedPasteEnabled;

    public int Columns => columns;
    public int Rows => rows;
    public int CursorX => x;
    public int CursorY => y;
    public bool IsViewportAtBottom => scrollbackOffset == 0;
    public double ViewportFraction => scrollbackOffset - Math.Floor(scrollbackOffset);
    public double ScrollbackOffset => scrollbackOffset;
    public int ScrollbackCount => scrollback.Count;
    public int TotalLineCount => scrollback.Count + rows;
    public int ViewportTopLine => scrollback.Count - (int)Math.Floor(scrollbackOffset);

    private enum ParserState
    {
        Normal,
        Escape,
        Csi,
        Osc,
        OscEscape,
        Charset
    }

    private ParserState state = ParserState.Normal;
    private string csi = "";

    public VtTerminalBuffer(
        int columns,
        int rows,
        Color defaultForeground,
        Color defaultBackground)
    {
        this.columns = Math.Clamp(columns, 1, 500);
        this.rows = Math.Clamp(rows, 1, 200);

        this.defaultForeground = defaultForeground;
        this.defaultBackground = defaultBackground;

        foreground = defaultForeground;
        background = defaultBackground;

        scrollTop = 0;
        scrollBottom = this.rows - 1;

        cells = CreateGrid(this.columns, this.rows);
    }

    private Cell[,] CreateGrid(int width, int height)
    {
        var grid = new Cell[width, height];
        var blank = new Cell(' ', defaultForeground, defaultBackground);

        for (int row = 0; row < height; row++)
        for (int col = 0; col < width; col++)
            grid[col, row] = blank;

        return grid;
    }

    public Cell GetCell(int column, int row)
    {
        column = Math.Clamp(column, 0, columns - 1);

        if (!alternateScreen && scrollbackOffset > 0)
        {
            int historyRow = scrollback.Count -
                (int)Math.Floor(scrollbackOffset) + row;
            if (historyRow < 0)
                return new Cell(' ', defaultForeground, defaultBackground);

            if (historyRow < scrollback.Count)
            {
                Cell[] line = scrollback[historyRow];
                return column < line.Length
                    ? line[column]
                    : new Cell(' ', defaultForeground, defaultBackground);
            }

            row = historyRow - scrollback.Count;
        }

        return row >= 0 && row < rows
            ? cells[column, row]
            : new Cell(' ', defaultForeground, defaultBackground);
    }

    public Cell GetHistoryCell(int column, int line)
    {
        if (column < 0 || column >= columns || line < 0 || line >= TotalLineCount)
            return new Cell(' ', defaultForeground, defaultBackground);

        return line < scrollback.Count
            ? scrollback[line][column]
            : cells[column, line - scrollback.Count];
    }

    public bool ScrollViewport(double lines)
    {
        if (alternateScreen || scrollback.Count == 0 || lines == 0)
            return false;

        return ScrollViewportTo(scrollbackOffset + lines);
    }

    public bool ScrollViewportTo(double offset)
    {
        if (alternateScreen)
            return false;

        double nextOffset = Math.Clamp(
            offset,
            0,
            scrollback.Count);
        if (nextOffset == scrollbackOffset)
            return false;

        scrollbackOffset = nextOffset;
        return true;
    }

    public void ScrollToBottom()
    {
        scrollbackOffset = 0;
    }

    public IReadOnlyList<string> GetRecentCellChanges()
    {
        return recentCellChanges.ToArray();
    }

    public void UpdateDefaultColors(Color newForeground, Color newBackground)
    {
        Color oldForeground = defaultForeground;
        Color oldBackground = defaultBackground;
        defaultForeground = newForeground;
        defaultBackground = newBackground;

        UpdateGridDefaultColors(cells, oldForeground, oldBackground);
        if (savedMainScreen is not null)
            UpdateGridDefaultColors(savedMainScreen, oldForeground, oldBackground);
        foreach (Cell[] line in scrollback)
        {
            for (int column = 0; column < line.Length; column++)
            {
                Cell cell = line[column];
                line[column] = cell with
                {
                    Foreground = cell.Foreground == oldForeground
                        ? newForeground : cell.Foreground,
                    Background = cell.Background == oldBackground
                        ? newBackground : cell.Background
                };
            }
        }

        if (foreground == oldForeground)
            foreground = newForeground;
        if (background == oldBackground)
            background = newBackground;
    }

    private void UpdateGridDefaultColors(
        Cell[,] grid,
        Color oldForeground,
        Color oldBackground)
    {
        for (int row = 0; row < grid.GetLength(1); row++)
        for (int column = 0; column < grid.GetLength(0); column++)
        {
            Cell cell = grid[column, row];
            Color foreground = cell.Foreground == oldForeground
                ? defaultForeground : cell.Foreground;
            Color background = cell.Background == oldBackground
                ? defaultBackground : cell.Background;

            if (foreground != cell.Foreground || background != cell.Background)
            {
                grid[column, row] = cell with
                {
                    Foreground = foreground,
                    Background = background
                };
            }
        }
    }

    public void Feed(string data)
    {
        foreach (char ch in data)
        {
            switch (state)
            {
                case ParserState.Normal:
                    ProcessCharacter(ch);
                    break;

                case ParserState.Escape:
                    ProcessEscape(ch);
                    break;

                case ParserState.Csi:
                    ProcessCsi(ch);
                    break;

                case ParserState.Osc:
                    if (ch == '\x07')
                        state = ParserState.Normal;
                    else if (ch == '\x1b')
                        state = ParserState.OscEscape;
                    break;

                case ParserState.OscEscape:
                    state = ch == '\\'
                        ? ParserState.Normal
                        : ParserState.Osc;
                    break;

                case ParserState.Charset:
                    // Preskoči znak za izbiro znakovne množice.
                    state = ParserState.Normal;
                    break;
            }
        }
    }

private static int GetUnicodeWidth(Rune rune)
{
    int c = rune.Value;
    UnicodeCategory category = Rune.GetUnicodeCategory(rune);

    if (category is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.EnclosingMark
        or UnicodeCategory.Format
        or UnicodeCategory.Control)
    {
        return 0;
    }

    if (c is >= 0x1100 and <= 0x115F
        || c is >= 0x2E80 and <= 0xA4CF && c != 0x303F
        || c is >= 0xAC00 and <= 0xD7A3
        || c is >= 0xF900 and <= 0xFAFF
        || c is >= 0xFE10 and <= 0xFE19
        || c is >= 0xFE30 and <= 0xFE6F
        || c is >= 0xFF00 and <= 0xFF60
        || c is >= 0xFFE0 and <= 0xFFE6
        || c is >= 0x1F300 and <= 0x1FAFF
        || c is >= 0x20000 and <= 0x3FFFD)
    {
        return 2;
    }

    return 1;
}

private void WriteRune(Rune rune)
{
    int width = GetUnicodeWidth(rune);

    // Kombinirani znaki se pripnejo prejšnjemu znaku.
    if (width == 0)
    {
        int previousX;
        int previousY;

        if (wrapPending || x > 0)
        {
            previousX = wrapPending ? columns - 1 : x - 1;
            previousY = y;
        }
        else if (y > 0)
        {
            previousX = columns - 1;
            previousY = y - 1;
        }
        else
        {
            return;
        }

        Cell previous = cells[previousX, previousY];

        if (previous.Width == 0 && previousX > 0)
        {
            previousX--;
            previous = cells[previousX, previousY];
        }

        cells[previousX, previousY] = previous with
        {
            Character = previous.Character + rune.ToString()
        };

        return;
    }

    if (wrapPending)
    {
        if (autoWrap)
        {
            x = 0;
            LineFeed();
        }

        wrapPending = false;
    }

    // Dvoceličnega znaka ne začnemo v zadnji celici vrstice,
    // če je omogočen samodejni prelom.
    if (width == 2 && x == columns - 1 && autoWrap)
    {
        x = 0;
        LineFeed();
    }

    // Če je terminal preozek ali je prelom izklopljen,
    // znaka ne zapisujemo čez rob matrike.
    if (width == 2 && x == columns - 1)
        width = 1;

    Cell oldCell = cells[x, y];
    ClearCell(x, y);

    cells[x, y] = new Cell(
        rune.ToString(),
        width,
        foreground,
        background
    );
    RecordCellChange(x, y, oldCell, cells[x, y], "WRITE");

    if (width == 2)
    {
        cells[x + 1, y] = new Cell(
            "",
            0,
            foreground,
            background
        );
    }

    if (x + width >= columns)
    {
        x = columns - 1;
        wrapPending = true;
    }
    else
    {
        x += width;
    }
}


private void ProcessCharacter(char ch)
{
    // UTF-16 znake, ki so sestavljeni iz dveh delov,
    // najprej sestavimo v en Unicode znak.
    if (pendingHighSurrogate != '\0')
    {
        if (char.IsLowSurrogate(ch))
        {
            Rune rune = new Rune(pendingHighSurrogate, ch);
            pendingHighSurrogate = '\0';
            WriteRune(rune);
            return;
        }

        // Prejšnji znak je bil nepopoln.
        WriteRune(new Rune('\uFFFD'));
        pendingHighSurrogate = '\0';
    }

    if (char.IsHighSurrogate(ch))
    {
        pendingHighSurrogate = ch;
        return;
    }

    if (char.IsLowSurrogate(ch))
    {
        WriteRune(new Rune('\uFFFD'));
        return;
    }

    if (ch == '\x1b')
    {
        state = ParserState.Escape;
        return;
    }

    switch (ch)
    {
        case '\0':
        case '\x7f':
            return;

        case '\r':
            x = 0;
            wrapPending = false;
            return;

        case '\n':
        case '\v':
        case '\f':
            wrapPending = false;
            LineFeed();
            return;

        case '\b':
            wrapPending = false;

            if (x > 0)
            {
                x--;

                // Če smo na nadaljevalni celici širokega znaka,
                // se premaknemo še na njegov začetek.
                if (cells[x, y].Width == 0 && x > 0)
                    x--;
            }

            return;

        case '\t':
            wrapPending = false;
            x = Math.Min(columns - 1, ((x / 8) + 1) * 8);
            return;

        default:
            if (ch < ' ')
                return;

            WriteRune(new Rune(ch));
            return;
    }
}


    private void ProcessEscape(char ch)
    {
        state = ParserState.Normal;

        // Non-cursor-changing escapes such as SGR must preserve delayed wrap.
        switch (ch)
        {
            case '[':
                csi = "";
                state = ParserState.Csi;
                break;

            case ']':
                state = ParserState.Osc;
                break;

            case '(':
            case ')':
            case '*':
            case '+':
                state = ParserState.Charset;
                break;

            case '7':
                savedX = x;
                savedY = y;
                break;

            case '8':
                wrapPending = false;
                x = savedX;
                y = savedY;
                ClampCursor();
                break;

            case 'D':
                wrapPending = false;
                LineFeed();
                break;

            case 'E':
                wrapPending = false;
                x = 0;
                LineFeed();
                break;

            case 'M':
                wrapPending = false;
                ReverseLineFeed();
                break;

            case 'c':
                Reset();
                break;

            case '=':
            case '>':
                // Sprejmi ukaze izbire tipkovnice.
                break;
        }
    }

    private void ProcessCsi(char ch)
    {
        // CSI se konča z bajtom v območju 0x40–0x7E.
        if (ch >= '@' && ch <= '~')
        {
            ExecuteCsi(ch, csi);
            state = ParserState.Normal;
            csi = "";
            return;
        }

        if (csi.Length < 256)
            csi += ch;
        else
        {
            state = ParserState.Normal;
            csi = "";
        }
    }

    public void Resize(int newColumns, int newRows)
    {
        newColumns = Math.Clamp(newColumns, 1, 500);
        newRows = Math.Clamp(newRows, 1, 200);

        if (newColumns == columns && newRows == rows)
            return;

        cells = ResizeGrid(cells, newColumns, newRows);

        if (savedMainScreen != null)
        {
            savedMainScreen = ResizeGrid(
                savedMainScreen,
                newColumns,
                newRows
            );
        }

        columns = newColumns;
        rows = newRows;

        scrollTop = 0;
        scrollBottom = rows - 1;

        x = Math.Clamp(x, 0, columns - 1);
        y = Math.Clamp(y, 0, rows - 1);
        savedX = Math.Clamp(savedX, 0, columns - 1);
        savedY = Math.Clamp(savedY, 0, rows - 1);
        mainScreenX = Math.Clamp(mainScreenX, 0, columns - 1);
        mainScreenY = Math.Clamp(mainScreenY, 0, rows - 1);

        wrapPending = false;
    }

    private Cell[,] ResizeGrid(Cell[,] source, int width, int height)
    {
        var resized = CreateGrid(width, height);

        int copyWidth = Math.Min(source.GetLength(0), width);
        int copyHeight = Math.Min(source.GetLength(1), height);

        for (int row = 0; row < copyHeight; row++)
        for (int col = 0; col < copyWidth; col++)
            resized[col, row] = source[col, row];

        return resized;
    }

    private void ExecuteCsi(char command, string parameters)
    {
        lastCsiCommand = $"CSI [{parameters}{command}]";

        bool privateMode = parameters.StartsWith("?");
        string cleaned = parameters.TrimStart('?', '>', '!', '=');

        int[] p = cleaned.Length == 0
            ? Array.Empty<int>()
            : cleaned.Split(';')
                .Select(s => int.TryParse(s, out int n) ? n : 0)
                .ToArray();

        int P(int index, int fallback = 1)
        {
            if (index >= p.Length || p[index] == 0)
                return fallback;

            return p[index];
        }

        // Premik kurzorja prekine morebitni zakasnjeni prelom.
        if (command is 'A' or 'B' or 'C' or 'D' or 'E' or 'F'
            or 'G' or 'H' or 'f' or 'd' or 'r' or 's' or 'u')
        {
            wrapPending = false;
        }

        switch (command)
        {
            case 'A':
                y -= P(0);
                break;

            case 'B':
                y += P(0);
                break;

            case 'C':
                x += P(0);
                break;

            case 'D':
                x -= P(0);
                break;

            case 'E':
                y += P(0);
                x = 0;
                break;

            case 'F':
                y -= P(0);
                x = 0;
                break;

            case 'G':
            case '`':
                x = P(0) - 1;
                break;

            case 'd':
                y = P(0) - 1;
                if (originMode)
                    y += scrollTop;
                break;

            case 'H':
            case 'f':
                y = P(0) - 1;
                x = P(1) - 1;

                if (originMode)
                    y += scrollTop;

                break;

            case 'J':
                EraseDisplay(p.Length > 0 ? p[0] : 0);
                break;

            case 'K':
                EraseLine(p.Length > 0 ? p[0] : 0);
                break;

            case '@':
                InsertCharacters(P(0));
                break;

            case 'P':
                DeleteCharacters(P(0));
                break;

            case 'X':
                EraseCharacters(P(0));
                break;

            case 'L':
                if (y >= scrollTop && y <= scrollBottom)
                    InsertLines(P(0));
                break;

            case 'M':
                if (y >= scrollTop && y <= scrollBottom)
                    DeleteLines(P(0));
                break;

            case 'S':
                ScrollUp(P(0));
                break;

            case 'T':
                ScrollDown(P(0));
                break;

            case 'r':
            {
                int top = P(0) - 1;
                int bottom = P(1, rows) - 1;

                if (top >= 0 && bottom < rows && top < bottom)
                {
                    scrollTop = top;
                    scrollBottom = bottom;
                }
                else
                {
                    scrollTop = 0;
                    scrollBottom = rows - 1;
                }

                x = 0;
                y = originMode ? scrollTop : 0;
                break;
            }

            case 'm':
                SetGraphicsMode(p);
                break;

            case 's':
                savedX = x;
                savedY = y;
                break;

            case 'u':
    // CSI ? u je zasebni ukaz (npr. Kitty keyboard protocol),
    // ne ukaz za obnovitev položaja kurzorja.
        if (!privateMode)
        {
            x = savedX;
            y = savedY;
        }
        break;

            case 'h':
            case 'l':
                if (privateMode)
                {
                    bool enable = command == 'h';

                    foreach (int mode in p)
                    {
                        switch (mode)
                        {
                            case 6: // Origin mode
                                originMode = enable;
                                x = 0;
                                y = enable ? scrollTop : 0;
                                break;

                            case 7: // Auto-wrap
                                autoWrap = enable;
                                break;

                            case 25: // Cursor visibility
                                CursorVisible = enable;
                                break;

                            case 2004: // Bracketed paste
                                bracketedPasteEnabled = enable;
                                break;

                            case 47:
                            case 1047:
                            case 1049:
                                if (enable)
                                    EnterAlternateScreen();
                                else
                                    ExitAlternateScreen();
                                break;
                        }
                    }
                }
                break;

            case 'n':
                // DSR zahteva odgovor lupini. Trenutno ga ne pošiljamo.
                break;

            case 'c':
                // DA zahteva odgovor terminala. Trenutno ga ne pošiljamo.
                break;
        }

        ClampCursor();
    }

    private void EnterAlternateScreen()
    {
        if (alternateScreen)
            return;

        savedMainScrollbackOffset = scrollbackOffset;
        scrollbackOffset = 0;
        savedMainScreen = (Cell[,])cells.Clone();
        mainScreenX = x;
        mainScreenY = y;

        savedX = x;
        savedY = y;

        alternateScreen = true;
        cells = CreateGrid(columns, rows);

        x = 0;
        y = 0;
        scrollTop = 0;
        scrollBottom = rows - 1;
        wrapPending = false;
    }

    private void ExitAlternateScreen()
    {
        if (!alternateScreen || savedMainScreen == null)
            return;

        cells = savedMainScreen;
        savedMainScreen = null;

        x = Math.Clamp(mainScreenX, 0, columns - 1);
        y = Math.Clamp(mainScreenY, 0, rows - 1);

        alternateScreen = false;
        scrollbackOffset = Math.Clamp(
            savedMainScrollbackOffset,
            0,
            scrollback.Count);
        savedMainScrollbackOffset = 0;
        scrollTop = 0;
        scrollBottom = rows - 1;
        wrapPending = false;
    }

    private void InsertCharacters(int count)
    {
        count = Math.Clamp(count, 1, columns - x);

        for (int col = columns - 1; col >= x + count; col--)
            cells[col, y] = cells[col - count, y];

        for (int col = x; col < Math.Min(columns, x + count); col++)
            ClearCell(col, y);
    }

    private void DeleteCharacters(int count)
    {
        count = Math.Clamp(count, 1, columns - x);

        for (int col = x; col < columns - count; col++)
            cells[col, y] = cells[col + count, y];

        for (int col = Math.Max(x, columns - count); col < columns; col++)
            ClearCell(col, y);
    }

    private void EraseCharacters(int count)
    {
        int end = Math.Min(columns, x + Math.Max(1, count));

        for (int col = x; col < end; col++)
            ClearCell(col, y);
    }

    private void InsertLines(int count)
    {
        count = Math.Clamp(count, 1, scrollBottom - y + 1);

        for (int i = 0; i < count; i++)
        {
            for (int row = scrollBottom; row > y; row--)
            for (int col = 0; col < columns; col++)
                cells[col, row] = cells[col, row - 1];

            ClearLine(y);
        }
    }

    private void DeleteLines(int count)
    {
        count = Math.Clamp(count, 1, scrollBottom - y + 1);

        for (int i = 0; i < count; i++)
        {
            for (int row = y; row < scrollBottom; row++)
            for (int col = 0; col < columns; col++)
                cells[col, row] = cells[col, row + 1];

            ClearLine(scrollBottom);
        }
    }

    private void ScrollUp(int count)
    {
        count = Math.Clamp(count, 1, scrollBottom - scrollTop + 1);

        for (int i = 0; i < count; i++)
        {
            if (!alternateScreen && scrollTop == 0 && scrollBottom == rows - 1)
                AddScrollbackRow(cells, scrollTop);

            for (int row = scrollTop; row < scrollBottom; row++)
            for (int col = 0; col < columns; col++)
                cells[col, row] = cells[col, row + 1];

            ClearLine(scrollBottom);
        }
    }

    private void AddScrollbackRow(Cell[,] source, int row)
    {
        var savedLine = new Cell[columns];
        for (int column = 0; column < columns; column++)
            savedLine[column] = source[column, row];

        scrollback.Add(savedLine);
        if (scrollbackOffset > 0)
            scrollbackOffset++;

        if (scrollback.Count > MaxScrollbackRows)
        {
            scrollback.RemoveAt(0);
            if (scrollbackOffset > 0)
                scrollbackOffset = Math.Max(0, scrollbackOffset - 1);
        }
    }

    private void ScrollDown(int count)
    {
        count = Math.Clamp(count, 1, scrollBottom - scrollTop + 1);

        for (int i = 0; i < count; i++)
        {
            for (int row = scrollBottom; row > scrollTop; row--)
            for (int col = 0; col < columns; col++)
                cells[col, row] = cells[col, row - 1];

            ClearLine(scrollTop);
        }
    }

    private void SetGraphicsMode(int[] p)
    {
        if (p.Length == 0)
        {
            ResetColors();
            return;
        }

        for (int i = 0; i < p.Length; i++)
        {
            int code = p[i];

            if (code == 0)
            {
                ResetColors();
            }
            else if (code == 39)
            {
                foreground = defaultForeground;
            }
            else if (code == 49)
            {
                background = defaultBackground;
            }
            else if (code >= 30 && code <= 37)
            {
                foreground = AnsiColor(code - 30, false);
            }
            else if (code >= 40 && code <= 47)
            {
                background = AnsiColor(code - 40, false);
            }
            else if (code >= 90 && code <= 97)
            {
                foreground = AnsiColor(code - 90, true);
            }
            else if (code >= 100 && code <= 107)
            {
                background = AnsiColor(code - 100, true);
            }
            else if ((code == 38 || code == 48) && i + 1 < p.Length)
            {
                bool isForeground = code == 38;
                int mode = p[++i];

                if (mode == 5 && i + 1 < p.Length)
                {
                    Color color = Ansi256(p[++i]);

                    if (isForeground)
                        foreground = color;
                    else
                        background = color;
                }
                else if (mode == 2 && i + 3 < p.Length)
                {
                    int r = Math.Clamp(p[++i], 0, 255);
                    int g = Math.Clamp(p[++i], 0, 255);
                    int b = Math.Clamp(p[++i], 0, 255);

                    Color color = Color.FromRgb((byte)r, (byte)g, (byte)b);

                    if (isForeground)
                        foreground = color;
                    else
                        background = color;
                }
            }
        }
    }

    private static Color AnsiColor(int index, bool bright)
    {
        Color[] normal =
        {
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(205, 49, 49),
            Color.FromRgb(13, 188, 121),
            Color.FromRgb(229, 229, 16),
            Color.FromRgb(36, 114, 200),
            Color.FromRgb(188, 63, 188),
            Color.FromRgb(17, 168, 205),
            Color.FromRgb(229, 229, 229)
        };

        Color[] light =
        {
            Color.FromRgb(102, 102, 102),
            Color.FromRgb(241, 76, 76),
            Color.FromRgb(35, 209, 139),
            Color.FromRgb(245, 245, 67),
            Color.FromRgb(59, 142, 234),
            Color.FromRgb(214, 112, 214),
            Color.FromRgb(41, 184, 219),
            Color.FromRgb(255, 255, 255)
        };

        return (bright ? light : normal)[Math.Clamp(index, 0, 7)];
    }

    private static Color Ansi256(int index)
    {
        index = Math.Clamp(index, 0, 255);

        if (index < 8)
            return AnsiColor(index, false);

        if (index < 16)
            return AnsiColor(index - 8, true);

        if (index < 232)
        {
            int n = index - 16;
            int r = n / 36;
            int g = (n / 6) % 6;
            int b = n % 6;

            byte R(int v) => (byte)(v == 0 ? 0 : 55 + v * 40);

            return Color.FromRgb(R(r), R(g), R(b));
        }

        byte gray = (byte)(8 + (index - 232) * 10);
        return Color.FromRgb(gray, gray, gray);
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseLine(0);

                for (int row = y + 1; row < rows; row++)
                    ClearLine(row);
                break;

            case 1:
                for (int row = 0; row < y; row++)
                    ClearLine(row);

                EraseLine(1);
                break;

            case 2:
            case 3:
                Clear();
                break;
        }
    }


private void EraseLine(int mode)
{
    if (y < 0 || y >= rows)
        return;

    switch (mode)
    {
        case 0: // Od kurzorja do konca vrstice
            for (int col = x; col < columns; col++)
                ClearCell(col, y);
            break;

        case 1: // Od začetka vrstice do kurzorja
            for (int col = 0; col <= x && col < columns; col++)
                ClearCell(col, y);
            break;

        case 2: // Celotna vrstica
            ClearLine(y);
            break;
    }
}


    private void ClearLine(int row)
    {
        if (row < 0 || row >= rows)
            return;

        for (int col = 0; col < columns; col++)
            ClearCell(col, row);
    }

    private void ClearCell(int col, int row)
{
    if (col < 0 || col >= columns || row < 0 || row >= rows)
        return;

    Cell old = cells[col, row];

    // Če brišemo nadaljevalno celico, pobriši tudi začetek
    // dvoceličnega znaka.
    if (old.Width == 0 && col > 0 && cells[col - 1, row].Width == 2)
    {
        cells[col - 1, row] =
            new Cell(" ", 1, foreground, background);
    }

    // Če brišemo začetek dvoceličnega znaka, pobriši tudi
    // njegovo nadaljevalno celico.
    if (old.Width == 2 && col + 1 < columns)
    {
        cells[col + 1, row] =
            new Cell(" ", 1, foreground, background);
    }

    cells[col, row] = new Cell(" ", 1, foreground, background);
    RecordCellChange(col, row, old, cells[col, row], "CLEAR");
}

    private void RecordCellChange(
        int column,
        int row,
        Cell before,
        Cell after,
        string operation)
    {
        if (column >= 4 ||
            (before.Character == after.Character &&
             before.Width == after.Width &&
             before.Foreground == after.Foreground &&
             before.Background == after.Background))
        {
            return;
        }

        recentCellChanges.Enqueue(
            $"{DateTime.Now:HH:mm:ss.fff} {operation} " +
            $"({column},{row}) cursor=({x},{y}) {lastCsiCommand}: " +
            $"{System.Text.Json.JsonSerializer.Serialize(before.Character)}" +
            $" -> {System.Text.Json.JsonSerializer.Serialize(after.Character)}"
        );

        while (recentCellChanges.Count > MaxCellChangeEvents)
            recentCellChanges.Dequeue();
    }

    private void LineFeed()
    {
        if (y < scrollTop || y > scrollBottom)
        {
            y = Math.Min(y + 1, rows - 1);
            return;
        }

        if (y < scrollBottom)
        {
            y++;
            return;
        }

        ScrollUp(1);
    }

    private void ReverseLineFeed()
    {
        if (y < scrollTop || y > scrollBottom)
        {
            y = Math.Max(y - 1, 0);
            return;
        }

        if (y > scrollTop)
        {
            y--;
            return;
        }

        ScrollDown(1);
    }

    public void Clear()
    {
        cells = CreateGrid(columns, rows);
        scrollback.Clear();
        scrollbackOffset = 0;
        savedMainScrollbackOffset = 0;
        x = 0;
        y = 0;
        wrapPending = false;
    }

    private void ResetColors()
    {
        foreground = defaultForeground;
        background = defaultBackground;
    }

    private void Reset()
    {
        Clear();
        ResetColors();

        savedX = 0;
        savedY = 0;
        CursorVisible = true;
        originMode = false;
        autoWrap = true;
        alternateScreen = false;
        savedMainScreen = null;

        scrollTop = 0;
        scrollBottom = rows - 1;
    }

    private void ClampCursor()
    {
        x = Math.Clamp(x, 0, columns - 1);
        y = Math.Clamp(y, 0, rows - 1);
    }
}