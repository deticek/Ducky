using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;

namespace ducky;

public class TerminalView : Control
{
    public Config conf = ConfigReder.GetConfig();

    private Typeface font => new Typeface(conf.FontFamily);
    public double FontSize
    {
        get => conf.FontSize;
        private set => conf.FontSize = value;
    }

    public void ChangeFontSize(bool zas){
        //Alert.Info(zas.ToString());
        if(zas){
            if(FontSize < 72){
                FontSize+=1;
            }
        }else{
            if(FontSize > 6){
                FontSize--;
            }
        }
        UpdateTerminalSize();
        InvalidateVisual();
    }

    private readonly VtTerminalBuffer terminal;
    private readonly StringBuilder oscSequence = new();

    private ConPtySession? shell;
    private bool readingOscSequence;
    private bool oscEscapePending;
    private bool isShutdown;

    private const double Padding = 0;
    private const double LineSpacing = 1.35;

    private bool terminalStarted;
    private int lastColumns;
    private int lastRows;

    private readonly object diagnosticLock = new();
    private readonly Queue<(long Sequence, string Output)> recentOutput = new();
    private long outputSequence;
    private long lastAppliedOutputSequence;

    private const int MaxDiagnosticChunks = 200;
    private int? selectionAnchor;
    private int? selectionEnd;
    private bool selectionDragging;
    private bool keyboardNavigationActive;

    public TerminalView(string? workingDirectory = null)
    {
        Focusable = true;
        ClipToBounds = true;
        WorkingDirectory = NormalizeWorkingDirectory(workingDirectory);

        terminal = new VtTerminalBuffer(
            120,
            30,
            Color.Parse(conf.TextColor),
            Color.Parse(conf.BackgroundColor)
        );

    }

    public string WorkingDirectory { get; private set; }
    public event Action<string>? WorkingDirectoryChanged;

    public void Shutdown()
    {
        if (isShutdown)
            return;

        isShutdown = true;
        shell?.Dispose();
        shell = null;
    }

    public void ApplyConfig(Config updatedConfig)
    {
        string activeShell = conf.Shell;
        string activeShellPath = conf.ShellPath;
        string activeShellArgs = conf.ShellArgs;

        conf = updatedConfig.Clone();
        conf.Shell = activeShell;
        conf.ShellPath = activeShellPath;
        conf.ShellArgs = activeShellArgs;

        try
        {
            terminal.UpdateDefaultColors(
                Color.Parse(conf.TextColor),
                Color.Parse(conf.BackgroundColor));
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not apply terminal colors from config: " + ex.Message);
        }

        UpdateTerminalSize();
        InvalidateVisual();
    }

    public void Preload()
    {
        if (terminalStarted)
            return;

        terminalStarted = true;
        StartShell();
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Dispatcher.UIThread.Post(() =>
        {
            UpdateTerminalSize();

            if (!terminalStarted)
            {
                terminalStarted = true;
                StartShell();
            }

            InvalidateVisual();
        });
    }

    private void StartShell()
    {
        try
        {
            shell = new ConPtySession(
                conf.Shell,
                conf.ShellArgs,
                conf.ShellPath,
                (short)terminal.Columns,
                (short)terminal.Rows,
                WorkingDirectory
            );

            shell.OutputReceived += output =>
{
                long sequence;

                lock (diagnosticLock)
                {
                    sequence = ++outputSequence;
                    recentOutput.Enqueue((sequence, output));

                    while (recentOutput.Count > MaxDiagnosticChunks)
                        recentOutput.Dequeue();
    }

    Dispatcher.UIThread.Post(() =>
    {
        terminal.Feed(output);
        ProcessWorkingDirectorySequences(output);
        lastAppliedOutputSequence = sequence;
        InvalidateVisual();
    });
};

            // Dogodek je priklopljen, preden začnemo brati.
            shell.StartReading();
            string? promptHook = GetWorkingDirectoryPromptHook();
            if (promptHook is not null)
            {
                try
                {
                    shell.WriteInput(promptHook);
                }
                catch (Exception ex)
                {
                    DuckyLog.Log(
                        "Could not enable shell working-directory reporting: " +
                        ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                "Could not start shell: " + ex);

            terminal.Feed(
                $"\r\nCould not start shell '{conf.Shell}': {ex.Message}\r\n"
            );
            InvalidateVisual();
            terminalStarted = false;
        }
    }

    private static string NormalizeWorkingDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return Path.GetFullPath(path);

        if (!string.IsNullOrWhiteSpace(path))
            DuckyLog.Log(
                $"Saved terminal working directory '{path}' does not exist; using the application directory.");

        return Environment.CurrentDirectory;
    }

    private string? GetWorkingDirectoryPromptHook()
    {
        string shellName = Path.GetFileName(
            string.IsNullOrWhiteSpace(conf.ShellPath) ? conf.Shell : conf.ShellPath
        ).ToLowerInvariant();

        if (shellName is "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe")
        {
            return "function global:prompt { $cwd = $PWD.ProviderPath; " +
                   "[Console]::Write(([char]27 + ']9;9;' + $cwd + [char]27 + '\\')); " +
                   "\"PS $($PWD.Path)> \" }\r";
        }

        if (shellName is "nu" or "nu.exe" or "nushell" or "nushell.exe")
        {
            return "$env.PROMPT_COMMAND = {|| " +
                   "$\"\\u{1b}]9;9;($env.PWD)\\u{1b}\\\\(ansi green_bold)($env.PWD)(ansi reset)> \" }\r";
        }

        return null;
    }

    private void ProcessWorkingDirectorySequences(string output)
    {
        foreach (char character in output)
        {
            if (!readingOscSequence)
            {
                if (character == '\x1b')
                {
                    oscEscapePending = true;
                    continue;
                }

                if (oscEscapePending && character == ']')
                {
                    readingOscSequence = true;
                    oscEscapePending = false;
                    oscSequence.Clear();
                    continue;
                }

                oscEscapePending = character == '\x1b';
                continue;
            }

            if (oscEscapePending)
            {
                if (character == '\\')
                {
                    ApplyWorkingDirectorySequence(oscSequence.ToString());
                    readingOscSequence = false;
                    oscEscapePending = false;
                    oscSequence.Clear();
                    continue;
                }

                oscSequence.Append('\x1b');
                oscEscapePending = false;
            }

            if (character == '\a')
            {
                ApplyWorkingDirectorySequence(oscSequence.ToString());
                readingOscSequence = false;
                oscSequence.Clear();
            }
            else if (character == '\x1b')
            {
                oscEscapePending = true;
            }
            else if (oscSequence.Length < 4096)
            {
                oscSequence.Append(character);
            }
            else
            {
                readingOscSequence = false;
                oscSequence.Clear();
            }
        }
    }

    private void ApplyWorkingDirectorySequence(string sequence)
    {
        string? path = null;
        if (sequence.StartsWith("9;9;", StringComparison.Ordinal))
        {
            path = sequence[4..];
        }
        else if (sequence.StartsWith("7;", StringComparison.Ordinal) &&
                 Uri.TryCreate(sequence[2..], UriKind.Absolute, out Uri? uri) &&
                 uri.IsFile)
        {
            path = uri.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            string normalizedPath = Path.GetFullPath(path);
            if (!Directory.Exists(normalizedPath) ||
                string.Equals(
                    WorkingDirectory,
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WorkingDirectory = normalizedPath;
            WorkingDirectoryChanged?.Invoke(normalizedPath);
        }
        catch (Exception ex)
        {
            DuckyLog.Log($"Could not update terminal working directory: {ex.Message}");
        }
    }

private double GetCellWidth()
{
    var sample = new FormattedText(
        "M",
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        font,
        FontSize,
        Brushes.White
    );

    double scale =
        TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    // Terminal potrebuje enakomerno mrežo celic.
    // Zaokrožimo širino navzgor na fizično slikovno piko.
    double width = sample.WidthIncludingTrailingWhitespace;

    return Math.Ceiling(width * scale) / scale;
}



    private double GetLineHeight()
    {
        var sample = new FormattedText(
            "Mg",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            font,
            FontSize,
            Brushes.White
        );

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

        return Math.Ceiling(sample.Height * scaling) / scaling;
    }

    private void UpdateTerminalSize()
    {
        if (Bounds.Width <= Padding * 2 ||
            Bounds.Height <= Padding * 2)
        {
            return;
        }

        double cellWidth = GetCellWidth();
        double lineHeight = GetLineHeight();

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

double availableWidth = Math.Floor(Bounds.Width * scaling) / scaling;
double availableHeight = Math.Floor(Bounds.Height * scaling) / scaling;

        int newColumns = Math.Clamp(
    (int)Math.Floor((availableWidth - Padding * 2) / cellWidth),
    1,
    500
);

int newRows = Math.Clamp(
    (int)Math.Floor((availableHeight - Padding * 2) / lineHeight),
    1,
    200
);

        if (newColumns == lastColumns && newRows == lastRows && terminal.Columns == newColumns && terminal.Rows == newRows)
        {
            return;
        }

        lastColumns = newColumns;
        lastRows = newRows;

        if (newColumns != terminal.Columns ||
            newRows != terminal.Rows)
        {

            terminal.Resize(newColumns, newRows);

            try
            {
                shell?.Resize(newColumns, newRows);
            }
            catch (Exception ex)
            {
                DuckyLog.Log("Terminal resize failed: " + ex.Message);
            }

            InvalidateVisual();
        }

try
{
    string logPath = DuckyPaths.LogFilePath;
    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

    File.AppendAllText(
        logPath,
        $"Bounds: {Bounds.Width:F2}x{Bounds.Height:F2}, " +
        $"Cell: {cellWidth:F2}x{lineHeight:F2}, " +
        $"Grid: {newColumns}x{newRows}, " +
        $"Unused right: {Bounds.Width - newColumns * cellWidth:F2}px, " +
        $"Unused bottom: {Bounds.Height - newRows * lineHeight:F2}px" +
        Environment.NewLine
    );
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine(ex.Message);
}

    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateTerminalSize();
    }

    


public override void Render(DrawingContext context)
{
    base.Render(context);

    Color background = Color.Parse(conf.BackgroundColor);
    Color foreground = Color.Parse(conf.TextColor);

    context.FillRectangle(
        new SolidColorBrush(Color.Parse(conf.BackgroundColor)),
        new Rect(0, 0, Bounds.Width, Bounds.Height)
    );

    double cellWidth = GetCellWidth();
    double lineHeight = GetLineHeight();
 // 1. Ozadja: vse meje celic so poravnane na mrežo.
for (int row = 0; row < terminal.Rows; row++)
{
    double y0 = Padding + row * lineHeight;
    double y1 = Padding + (row + 1) * lineHeight;

    for (int col = 0; col < terminal.Columns; col++)
    {
        var cell = terminal.GetCell(col, row);

        double x0 = Padding + col * cellWidth;
        double x1 = Padding + (col + 1) * cellWidth;
        double yEnd = y1;

        // Zadnji stolpec sega do desnega roba.
        if (col == terminal.Columns - 1)
        {
            x1 = Bounds.Width;
        }

        // Zadnja vrstica sega do spodnjega roba.
        if (row == terminal.Rows - 1)
        {
            yEnd = Bounds.Height;
        }

        context.FillRectangle(
            new SolidColorBrush(cell.Background),
            new Rect(
                x0,
                y0,
                x1 - x0,
                yEnd - y0
            )
        );
    }
}

// Zapolni morebitno režo v skrajnem spodnjem desnem kotu.
context.FillRectangle(
    new SolidColorBrush(Color.Parse(conf.BackgroundColor)),
    new Rect(
        Math.Max(0, Bounds.Width - 2),
        Math.Max(0, Bounds.Height - 2),
        2,
        2
    )
);

    DrawSelection(context, cellWidth, lineHeight);

    // 2. Znaki: enake koordinate kot pri ozadjih.
    for (int row = 0; row < terminal.Rows; row++)
    {
        double y0 = Padding + row * lineHeight;
        double y1 = Padding + (row + 1) * lineHeight;

        for (int col = 0; col < terminal.Columns; col++)
        {
            var cell = terminal.GetCell(col, row);

            if (cell.Width <= 0 ||
                string.IsNullOrEmpty(cell.Character) ||
                cell.Character == " ")
            {
                continue;
            }

            double x0 = Padding + col * cellWidth;
            double x1 = Padding + (col + cell.Width) * cellWidth;

            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

            // Poravnaj položaj na fizične slikovne pike.
            //double drawX = Math.Round(x0 * scale) / scale;
            //double drawY = Math.Round(y0 * scale) / scale;

            double drawX = x0;
            double drawY = y0;

            Rect textRect = new Rect(
                x0,
                y0,
                x1 - x0,
                y1 - y0
            );

            var formattedText = new FormattedText(
                cell.Character,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                font,
                FontSize,
                new SolidColorBrush(
                    IsCellSelected(col, row) ? Colors.White : cell.Foreground)
            );

            using (context.PushClip(textRect))
            {
                context.DrawText(
                    formattedText,
                    new Point(drawX, drawY)
                );
            }
        }
    }

    // 3. Kazalec.
    if (!terminal.CursorVisible)
        return;

    int cursorPosition = keyboardNavigationActive && selectionEnd.HasValue
        ? selectionEnd.Value
        : terminal.CursorY * terminal.Columns + terminal.CursorX;
    cursorPosition = Math.Clamp(cursorPosition, 0, terminal.Columns * terminal.Rows - 1);
    int cursorCol = cursorPosition % terminal.Columns;
    int cursorRow = cursorPosition / terminal.Columns;

    var cursorCell = terminal.GetCell(cursorCol, cursorRow);

    if (cursorCell.Width == 0 && cursorCol > 0)
    {
        cursorCol--;
        cursorCell = terminal.GetCell(cursorCol, cursorRow);
    }

    int cursorWidth = Math.Max(1, cursorCell.Width);

    double cx = Padding + cursorCol * cellWidth;
    double cy = Padding + cursorRow * lineHeight;

    Rect cursorRect = new Rect(
        cx,
        cy,
        cursorWidth * cellWidth,
        lineHeight
    );

    context.FillRectangle(
        new SolidColorBrush(foreground),
        cursorRect
    );

    if (!string.IsNullOrEmpty(cursorCell.Character) &&
        cursorCell.Character != " ")
    {
        using (context.PushClip(cursorRect))
        {
            context.DrawText(
                new FormattedText(
                    cursorCell.Character,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    font,
                    FontSize,
                    new SolidColorBrush(background)
                ),
                new Point(cx, cy)
            );
        }
    }
}



    private int GetCellAt(Point position)
    {
        double cellWidth = GetCellWidth();
        double lineHeight = GetLineHeight();

        int column = Math.Clamp(
            (int)(position.X / cellWidth), 0, terminal.Columns - 1);
        int row = Math.Clamp(
            (int)(position.Y / lineHeight), 0, terminal.Rows - 1);
        return row * terminal.Columns + column;
    }

    private bool HasSelection =>
        selectionAnchor.HasValue &&
        selectionEnd.HasValue &&
        selectionAnchor.Value != selectionEnd.Value;

    private bool IsCellSelected(int column, int row)
    {
        if (!HasSelection)
            return false;

        int anchor = selectionAnchor!.Value;
        int end = selectionEnd!.Value;
        int cell = row * terminal.Columns + column;

        return cell >= Math.Min(anchor, end) && cell < Math.Max(anchor, end);
    }

    private void DrawSelection(
        DrawingContext context,
        double cellWidth,
        double lineHeight)
    {
        if (!HasSelection)
            return;

        int anchor = selectionAnchor!.Value;
        int end = selectionEnd!.Value;
        int first = Math.Min(anchor, end);
        int last = Math.Max(anchor, end) - 1;

        if (last < first)
            return;

        for (int row = first / terminal.Columns;
             row <= last / terminal.Columns;
             row++)
        {
            int firstColumn = row == first / terminal.Columns
                ? first % terminal.Columns
                : 0;
            int lastColumn = row == last / terminal.Columns
                ? last % terminal.Columns
                : terminal.Columns - 1;

            context.FillRectangle(
                new SolidColorBrush(Color.Parse("#264F78")),
                new Rect(
                    Padding + firstColumn * cellWidth,
                    Padding + row * lineHeight,
                    (lastColumn - firstColumn + 1) * cellWidth,
                    lineHeight
                )
            );
        }
    }

    private string GetSelectedText()
    {
        if (!HasSelection)
            return string.Empty;

        int anchor = selectionAnchor!.Value;
        int end = selectionEnd!.Value;
        int first = Math.Min(anchor, end);
        int last = Math.Max(anchor, end) - 1;
        var text = new StringBuilder();

        if (last < first)
            return string.Empty;

        for (int row = first / terminal.Columns;
             row <= last / terminal.Columns;
             row++)
        {
            int firstColumn = row == first / terminal.Columns
                ? first % terminal.Columns
                : 0;
            int lastColumn = row == last / terminal.Columns
                ? last % terminal.Columns
                : terminal.Columns - 1;
            var line = new StringBuilder();

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                VtTerminalBuffer.Cell cell = terminal.GetCell(column, row);

                if (cell.Width > 0)
                    line.Append(cell.Character);
            }

            text.Append(line.ToString().TrimEnd());

            if (row < last / terminal.Columns)
                text.AppendLine();
        }

        return text.ToString();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            _ = PasteClipboardAsync();
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed)
            return;

        Focus();
        selectionAnchor = GetCellAt(e.GetPosition(this));
        selectionEnd = selectionAnchor + 1;
        selectionDragging = true;
        keyboardNavigationActive = false;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!selectionDragging)
            return;

        int cell = GetCellAt(e.GetPosition(this));
        selectionEnd = cell >= selectionAnchor
            ? cell + 1
            : cell;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!selectionDragging)
            return;

        int cell = GetCellAt(e.GetPosition(this));
        selectionEnd = cell >= selectionAnchor
            ? cell + 1
            : cell;
        selectionDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }

    private void Send(string value)
    {
        try
        {
            shell?.WriteInput(value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                "Could not send terminal input: " + ex.Message);
        }
    }

    private async Task CopySelectionAsync()
    {
        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            string selectedText = GetSelectedText();

            if (clipboard != null && selectedText.Length > 0)
                await clipboard.SetTextAsync(selectedText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                "Could not copy terminal selection: " + ex.Message);
        }
    }

    private async Task PasteClipboardAsync()
    {
        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return;

            string? text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrEmpty(text))
                return;

            selectionAnchor = null;
            selectionEnd = null;
            InvalidateVisual();

            Send(terminal.BracketedPasteEnabled
                ? $"\x1b[200~{text}\x1b[201~"
                : text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                "Could not paste into terminal: " + ex.Message);
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (!string.IsNullOrEmpty(e.Text))
        {
            ClearSelection();
            Send(e.Text);
            e.Handled = true;
        }
    }


    private bool leftCtrl, rightCtrl;
    private bool leftShift, rightShift;
    private bool leftAlt, rightAlt;

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        switch (e.Key)
        {
            case Key.LeftCtrl:   leftCtrl = false; break;
            case Key.RightCtrl:  rightCtrl = false; break;
            case Key.LeftShift:  leftShift = false; break;
            case Key.RightShift: rightShift = false; break;
            case Key.LeftAlt:    leftAlt = false; break;
            case Key.RightAlt:   rightAlt = false; break;
        }
    }

    public void CopyFun(){
        if (HasSelection)
                _ = CopySelectionAsync();
            else
                Send("\x03");
            return;
    }

    public void PasteFun(){
        _ = PasteClipboardAsync();
        return;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.Key == Key.Tab)
        {
            if (e.KeyModifiers == KeyModifiers.None)
            {
                ClearSelection();
                Send("\t");
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key is Key.Left or Key.Right)
        {
            MoveSelection(e.Key, shift, byWord: true);
            e.Handled = true;
            return;
        }

        if (shift && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            MoveSelection(e.Key, extend: true, byWord: false);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.D)
        {
            Send("\x04");
            e.Handled = true;
            return;
        }

        string? sequence = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7f",
            Key.Escape => "\x1b",
            Key.Up => "\x1b[A",
            Key.Down => "\x1b[B",
            Key.Right => "\x1b[C",
            Key.Left => "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.Delete => "\x1b[3~",
            Key.Insert => "\x1b[2~",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            _ => null
        };

        if (sequence != null)
        {
            ClearSelection();
            Send(sequence);
            e.Handled = true;
        }
    }

    private void ClearSelection()
    {
        if (!selectionAnchor.HasValue && !selectionEnd.HasValue)
            return;

        selectionAnchor = null;
        selectionEnd = null;
        keyboardNavigationActive = false;
        InvalidateVisual();
    }

    private void MoveSelection(Key key, bool extend, bool byWord)
    {
        int cursor = terminal.CursorY * terminal.Columns + terminal.CursorX;
        int end = keyboardNavigationActive && selectionEnd.HasValue
            ? selectionEnd.Value
            : cursor;

        if (extend && !selectionAnchor.HasValue)
            selectionAnchor = end;
        else if (!extend)
            selectionAnchor = null;

        int next = byWord
            ? MoveByWord(end, key == Key.Left ? -1 : 1)
            : Math.Clamp(end + key switch
            {
                Key.Left => -1,
                Key.Right => 1,
                Key.Up => -terminal.Columns,
                Key.Down => terminal.Columns,
                _ => 0
            }, 0, terminal.Columns * terminal.Rows);

        selectionEnd = next;
        keyboardNavigationActive = true;
        InvalidateVisual();
    }

    private int MoveByWord(int position, int direction)
    {
        int limit = terminal.Columns * terminal.Rows;

        if (direction < 0)
        {
            int index = position - 1;

            while (index >= 0 && !IsWordCell(index))
                index--;
            while (index >= 0 && IsWordCell(index))
                index--;

            return index + 1;
        }

        int next = Math.Clamp(position, 0, limit);

        if (next < limit && IsWordCell(next))
        {
            while (next < limit && IsWordCell(next))
                next++;
        }

        while (next < limit && !IsWordCell(next))
            next++;

        return next;
    }

    private bool IsWordCell(int position)
    {
        if (position < 0 || position >= terminal.Columns * terminal.Rows)
            return false;

        VtTerminalBuffer.Cell cell = terminal.GetCell(
            position % terminal.Columns,
            position / terminal.Columns
        );

        if (cell.Width <= 0 || string.IsNullOrEmpty(cell.Character))
            return false;

        Rune rune = Rune.GetRuneAt(cell.Character, 0);
        return Rune.IsLetterOrDigit(rune) || rune.Value == '_';
    }
}