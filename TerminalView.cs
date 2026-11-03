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
    private Point selectionPointerPosition;
    private DispatcherTimer? selectionAutoScrollTimer;

    private readonly StringBuilder commandInput = new();
    private int commandInputCursor;
    private readonly List<string> commandHistory = new();
    private int commandHistoryPosition = -1;
    private string historyDraft = "";
    private bool historyStartedAtPrompt;
    private bool canStartDuckyCommand = true;
    private bool isEditingDuckyCommand;
    private bool insertMode = true;
    private DispatcherTimer? scrollAnimationTimer;
    private double scrollTargetOffset;

    public TerminalView(string? workingDirectory = null)
    {
        Focusable = true;
        IsTabStop = true;
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
        scrollAnimationTimer?.Stop();
        selectionAutoScrollTimer?.Stop();
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
        int previousScrollbackCount = terminal.ScrollbackCount;
        bool followingScrollback = terminal.ScrollbackOffset > 0 ||
            scrollTargetOffset > 0;
        terminal.Feed(output);
        if (followingScrollback)
        {
            scrollTargetOffset = Math.Min(
                terminal.ScrollbackCount,
                scrollTargetOffset +
                    terminal.ScrollbackCount - previousScrollbackCount);
        }
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

//    File.AppendAllText(
//        logPath,
//        $"Bounds: {Bounds.Width:F2}x{Bounds.Height:F2}, " +
//        $"Cell: {cellWidth:F2}x{lineHeight:F2}, " +
//        $"Grid: {newColumns}x{newRows}, " +
//        $"Unused right: {Bounds.Width - newColumns * cellWidth:F2}px, " +
//        $"Unused bottom: {Bounds.Height - newRows * lineHeight:F2}px" +
//        Environment.NewLine
//    );
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
    double scrollTranslation = terminal.ViewportFraction * lineHeight;
 // 1. Ozadja: vse meje celic so poravnane na mrežo.
for (int row = -1; row <= terminal.Rows; row++)
{
    double y0 = Padding + row * lineHeight + scrollTranslation;
    double y1 = Padding + (row + 1) * lineHeight + scrollTranslation;

    for (int col = 0; col < terminal.Columns; col++)
    {
        var cell = terminal.GetCell(col, row);

        double x0 = Padding + col * cellWidth;
        double x1 = Padding + (col + 1) * cellWidth;
        // Zadnji stolpec sega do desnega roba.
        if (col == terminal.Columns - 1)
        {
            x1 = Bounds.Width;
        }

        context.FillRectangle(
            new SolidColorBrush(cell.Background),
            new Rect(
                x0,
                y0,
                x1 - x0,
                y1 - y0
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
    for (int row = -1; row <= terminal.Rows; row++)
    {
        double y0 = Padding + row * lineHeight + scrollTranslation;
        double y1 = Padding + (row + 1) * lineHeight + scrollTranslation;

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

    if (isEditingDuckyCommand)
    {
        if (!terminal.IsViewportAtBottom)
            return;

        DrawDuckyCommandInput(context, cellWidth, lineHeight, foreground, background);
        return;
    }

    // 3. Kazalec.
    if (!terminal.CursorVisible || !terminal.IsViewportAtBottom)
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

    private void DrawDuckyCommandInput(
        DrawingContext context,
        double cellWidth,
        double lineHeight,
        Color foreground,
        Color background)
    {
        int column = terminal.CursorX;
        int row = terminal.CursorY;
        int cursorColumn = column;
        int cursorRow = row;
        bool cursorPositionFound = commandInputCursor == 0;
        int inputOffset = 0;
        string text = commandInput.ToString();

        foreach (Rune rune in text.EnumerateRunes())
        {
            string character = rune.ToString();
            var formattedText = new FormattedText(
                character,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                font,
                FontSize,
                new SolidColorBrush(foreground));
            int characterCells = Math.Clamp(
                (int)Math.Round(formattedText.WidthIncludingTrailingWhitespace / cellWidth),
                1,
                2);

            if (column + characterCells > terminal.Columns)
            {
                column = 0;
                row++;
            }

            if (!cursorPositionFound && commandInputCursor <= inputOffset)
            {
                cursorColumn = column;
                cursorRow = row;
                cursorPositionFound = true;
            }

            if (row < terminal.Rows)
            {
                var cellRect = new Rect(
                    Padding + column * cellWidth,
                    Padding + row * lineHeight,
                    characterCells * cellWidth,
                    lineHeight);
                context.FillRectangle(new SolidColorBrush(background), cellRect);
                using (context.PushClip(cellRect))
                    context.DrawText(formattedText, new Point(cellRect.X, cellRect.Y));
            }

            column += characterCells;
            if (column >= terminal.Columns)
            {
                column = 0;
                row++;
            }

            inputOffset += rune.Utf16SequenceLength;
        }

        if (!cursorPositionFound)
        {
            cursorColumn = column;
            cursorRow = row;
        }

        if (cursorRow >= terminal.Rows)
            return;

        var cursorRect = new Rect(
            Padding + cursorColumn * cellWidth,
            Padding + cursorRow * lineHeight,
            cellWidth,
            lineHeight);
        context.FillRectangle(new SolidColorBrush(foreground), cursorRect);

        if (commandInputCursor < text.Length)
        {
            string nextCharacter = char.IsHighSurrogate(text[commandInputCursor]) &&
                                   commandInputCursor + 1 < text.Length
                ? text.Substring(commandInputCursor, 2)
                : text[commandInputCursor].ToString();
            using (context.PushClip(cursorRect))
            {
                context.DrawText(
                    new FormattedText(
                        nextCharacter,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        font,
                        FontSize,
                        new SolidColorBrush(background)),
                    new Point(cursorRect.X, cursorRect.Y));
            }
        }
    }



    private int GetCellAt(Point position)
    {
        double cellWidth = GetCellWidth();
        double lineHeight = GetLineHeight();

        int column = Math.Clamp(
            (int)(position.X / cellWidth), 0, terminal.Columns - 1);
        int visibleRow = (int)Math.Floor(
            (position.Y - terminal.ViewportFraction * lineHeight) / lineHeight);
        int line = Math.Clamp(
            terminal.ViewportTopLine + visibleRow,
            0,
            terminal.TotalLineCount - 1);
        return line * terminal.Columns + column;
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
        int line = terminal.ViewportTopLine + row;
        int cell = line * terminal.Columns + column;

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

        int firstLine = first / terminal.Columns;
        int lastLine = last / terminal.Columns;
        int firstVisibleLine = terminal.ViewportTopLine;
        int lastVisibleLine = firstVisibleLine + terminal.Rows;

        for (int line = Math.Max(firstLine, firstVisibleLine);
             line <= Math.Min(lastLine, lastVisibleLine);
             line++)
        {
            int firstColumn = line == firstLine
                ? first % terminal.Columns
                : 0;
            int lastColumn = line == lastLine
                ? last % terminal.Columns
                : terminal.Columns - 1;
            int visibleRow = line - firstVisibleLine;
            double y = Padding + visibleRow * lineHeight +
                terminal.ViewportFraction * lineHeight;

            context.FillRectangle(
                new SolidColorBrush(Color.Parse("#264F78")),
                new Rect(
                    Padding + firstColumn * cellWidth,
                    y,
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

        for (int lineIndex = first / terminal.Columns;
             lineIndex <= last / terminal.Columns;
             lineIndex++)
        {
            int firstColumn = lineIndex == first / terminal.Columns
                ? first % terminal.Columns
                : 0;
            int lastColumn = lineIndex == last / terminal.Columns
                ? last % terminal.Columns
                : terminal.Columns - 1;
            var lineText = new StringBuilder();

            for (int column = firstColumn; column <= lastColumn; column++)
            {
                VtTerminalBuffer.Cell cell = terminal.GetHistoryCell(column, lineIndex);

                if (cell.Width > 0)
                    lineText.Append(cell.Character);
            }

            text.Append(lineText.ToString().TrimEnd());

            if (lineIndex < last / terminal.Columns)
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
        selectionPointerPosition = e.GetPosition(this);
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

        selectionPointerPosition = e.GetPosition(this);
        UpdateSelectionEnd();
        UpdateSelectionAutoScroll();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (terminal.ScrollbackCount == 0)
            return;

        RequestViewportScroll(e.Delta.Y * 8);
        e.Handled = true;
    }

    private void RequestViewportScroll(double amount)
    {
        if (scrollAnimationTimer is null)
        {
            scrollAnimationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            scrollAnimationTimer.Tick += AnimateViewportScroll;
        }

        if (scrollAnimationTimer.IsEnabled)
            scrollTargetOffset += amount;
        else
            scrollTargetOffset = terminal.ScrollbackOffset + amount;

        scrollTargetOffset = Math.Clamp(
            scrollTargetOffset,
            0,
            terminal.ScrollbackCount);
        scrollAnimationTimer.Start();
    }

    private void AnimateViewportScroll(object? sender, EventArgs e)
    {
        scrollTargetOffset = Math.Clamp(
            scrollTargetOffset,
            0,
            terminal.ScrollbackCount);
        double distance = scrollTargetOffset - terminal.ScrollbackOffset;
        if (Math.Abs(distance) < 0.02)
        {
            terminal.ScrollViewportTo(scrollTargetOffset);
            scrollAnimationTimer?.Stop();
        }
        else
        {
            terminal.ScrollViewport(distance * 0.3);
        }

        InvalidateVisual();
    }

    private void UpdateSelectionEnd()
    {
        int cell = GetCellAt(selectionPointerPosition);
        selectionEnd = cell >= selectionAnchor
            ? cell + 1
            : cell;
    }

    private void UpdateSelectionAutoScroll()
    {
        if (!selectionDragging)
        {
            selectionAutoScrollTimer?.Stop();
            return;
        }

        double edgeSize = Math.Min(GetLineHeight(), Bounds.Height / 2);
        bool nearVerticalEdge =
            selectionPointerPosition.Y < edgeSize ||
            selectionPointerPosition.Y > Bounds.Height - edgeSize;
        if (!nearVerticalEdge)
        {
            selectionAutoScrollTimer?.Stop();
            return;
        }

        if (selectionAutoScrollTimer is null)
        {
            selectionAutoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            selectionAutoScrollTimer.Tick += AutoScrollSelection;
        }

        selectionAutoScrollTimer.Start();
    }

    private void AutoScrollSelection(object? sender, EventArgs e)
    {
        bool atTop = selectionPointerPosition.Y < GetLineHeight();
        double direction = atTop ? 1 : -1;
        double target = scrollAnimationTimer is { IsEnabled: true }
            ? scrollTargetOffset
            : terminal.ScrollbackOffset;

        if ((atTop && target >= terminal.ScrollbackCount) ||
            (!atTop && target <= 0))
        {
            selectionAutoScrollTimer?.Stop();
            return;
        }

        RequestViewportScroll(direction * 0.6);
        UpdateSelectionEnd();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!selectionDragging)
            return;

        selectionPointerPosition = e.GetPosition(this);
        UpdateSelectionEnd();
        selectionDragging = false;
        selectionAutoScrollTimer?.Stop();
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }

    private void NavigateHistory(bool up)
    {
        if (commandHistory.Count == 0)
            return;

        if (up)
        {
            if (commandHistoryPosition == -1)
            {
                historyDraft = commandInput.ToString();
                commandHistoryPosition = commandHistory.Count - 1;
            }
            else if (commandHistoryPosition > 0)
            {
                commandHistoryPosition--;
            }
        }
        else
        {
            if (commandHistoryPosition == -1)
                return;

            if (commandHistoryPosition < commandHistory.Count - 1)
            {
                commandHistoryPosition++;
            }
            else
            {
                commandHistoryPosition = -1;
                commandInput.Clear();
                commandInput.Append(historyDraft);
                commandInputCursor = commandInput.Length;
                if (historyStartedAtPrompt)
                {
                    isEditingDuckyCommand = false;
                    historyStartedAtPrompt = false;
                }
                InvalidateVisual();
                return;
            }
        }

        commandInput.Clear();
        commandInput.Append(commandHistory[commandHistoryPosition]);
        commandInputCursor = commandInput.Length;
        InvalidateVisual();
    }

    private async Task ExecuteDuckyCommand(string command)
    {
        CommandResult result = await MainWindow.Commands.ExecuteAsync(
            command,
            new CommandContext("Terminal"));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            terminal.Feed($"{result.Message}\r\n");
            InvalidateVisual();

            Send("\r");
        });
    }

    private bool IsCommandWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    private void InsertDuckyInput(string input)
    {
        var textBuilder = new StringBuilder(input.Length);
        foreach (char character in input)
        {
            if (!char.IsControl(character))
                textBuilder.Append(character);
        }

        string text = textBuilder.ToString();
        if (text.Length == 0)
            return;

        if (insertMode || commandInputCursor >= commandInput.Length)
            commandInput.Insert(commandInputCursor, text);
        else
        {
            int replacedLength = Math.Min(text.Length, commandInput.Length - commandInputCursor);
            commandInput.Remove(commandInputCursor, replacedLength);
            commandInput.Insert(commandInputCursor, text);
        }

        commandInputCursor += text.Length;
        commandHistoryPosition = -1;
        InvalidateVisual();
    }

    private void SubmitDuckyCommand()
    {
        string command = commandInput.ToString();
        if (!string.IsNullOrWhiteSpace(command))
            commandHistory.Add(command);

        terminal.Feed(command + "\r\n");
        commandInput.Clear();
        commandInputCursor = 0;
        commandHistoryPosition = -1;
        historyDraft = "";
        historyStartedAtPrompt = false;
        isEditingDuckyCommand = false;
        InvalidateVisual();

        _ = ExecuteDuckyCommand(command);
    }

    private void CancelDuckyCommand()
    {
        commandInput.Clear();
        commandInputCursor = 0;
        commandHistoryPosition = -1;
        historyDraft = "";
        historyStartedAtPrompt = false;
        isEditingDuckyCommand = false;
        InvalidateVisual();
    }

    private void MoveDuckyCursorByWord(int direction)
    {
        int next = commandInputCursor;
        if (direction < 0)
        {
            while (next > 0 && !IsCommandWordCharacter(commandInput[next - 1]))
                next--;
            while (next > 0 && IsCommandWordCharacter(commandInput[next - 1]))
                next--;
        }
        else
        {
            while (next < commandInput.Length && IsCommandWordCharacter(commandInput[next]))
                next++;
            while (next < commandInput.Length && !IsCommandWordCharacter(commandInput[next]))
                next++;
        }

        commandInputCursor = next;
        InvalidateVisual();
    }

    private void Send(string value)
    {
        try
        {
            scrollAnimationTimer?.Stop();
            scrollTargetOffset = 0;
            terminal.ScrollToBottom();

            if (isEditingDuckyCommand)
            {
                InsertDuckyInput(value);
                return;
            }

            if (canStartDuckyCommand &&
                value.StartsWith("/", StringComparison.Ordinal) &&
                value.IndexOfAny(['\r', '\n']) < 0)
            {
                isEditingDuckyCommand = true;
                commandInput.Clear();
                commandInputCursor = 0;
                InsertDuckyInput(value);
                return;
            }

            shell?.WriteInput(value);
            UpdateShellLineState(value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                "Could not send terminal input: " + ex.Message);
        }
    }

    private static bool EndsAtNewLine(string value)
    {
        int lastLineBreak = Math.Max(value.LastIndexOf('\r'), value.LastIndexOf('\n'));
        return lastLineBreak == value.Length - 1;
    }

    private void UpdateShellLineState(string input)
    {
        if (input == "\x03")
        {
            canStartDuckyCommand = true;
            return;
        }

        if (input.Contains('\r') || input.Contains('\n'))
        {
            canStartDuckyCommand = EndsAtNewLine(input);
            return;
        }

        foreach (char character in input)
        {
            if (char.IsControl(character))
                continue;

            canStartDuckyCommand = false;
            return;
        }
    }

    private void HandleDuckyCommandKey(KeyEventArgs e)
    {
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool handled = true;

        if (control && e.Key == Key.C)
        {
            CancelDuckyCommand();
        }
        else if (control && e.Key == Key.V)
        {
            _ = PasteClipboardAsync();
        }
        else
        {
            switch (e.Key)
            {
                case Key.Enter:
                    SubmitDuckyCommand();
                    break;
                case Key.Escape:
                    CancelDuckyCommand();
                    break;
                case Key.Back:
                    if (commandInputCursor > 0)
                    {
                        commandInput.Remove(commandInputCursor - 1, 1);
                        commandInputCursor--;
                        InvalidateVisual();
                    }
                    break;
                case Key.Delete:
                    if (commandInputCursor < commandInput.Length)
                    {
                        commandInput.Remove(commandInputCursor, 1);
                        InvalidateVisual();
                    }
                    break;
                case Key.Left:
                    if (control)
                        MoveDuckyCursorByWord(-1);
                    else if (commandInputCursor > 0)
                    {
                        commandInputCursor--;
                        InvalidateVisual();
                    }
                    break;
                case Key.Right:
                    if (control)
                        MoveDuckyCursorByWord(1);
                    else if (commandInputCursor < commandInput.Length)
                    {
                        commandInputCursor++;
                        InvalidateVisual();
                    }
                    break;
                case Key.Home:
                    commandInputCursor = 0;
                    InvalidateVisual();
                    break;
                case Key.End:
                    commandInputCursor = commandInput.Length;
                    InvalidateVisual();
                    break;
                case Key.Insert:
                    insertMode = !insertMode;
                    InvalidateVisual();
                    break;
                case Key.Up:
                    NavigateHistory(up: true);
                    break;
                case Key.Down:
                    NavigateHistory(up: false);
                    break;
                default:
                    handled = false;
                    break;
            }
        }

        if (handled)
            e.Handled = true;
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

            if (isEditingDuckyCommand)
                InsertDuckyInput(text);
            else if (canStartDuckyCommand &&
                     text.StartsWith("/", StringComparison.Ordinal) &&
                     text.IndexOfAny(['\r', '\n']) < 0)
                Send(text);
            else
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

        if(string.IsNullOrEmpty(e.Text))
            return;

        ClearSelection();

        Send(e.Text);
        e.Handled = true;
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
        if (isEditingDuckyCommand)
        {
            CancelDuckyCommand();
            return;
        }

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

        if (!isEditingDuckyCommand &&
            e.Key == Key.Up &&
            canStartDuckyCommand &&
            commandHistory.Count > 0)
        {
            isEditingDuckyCommand = true;
            commandInput.Clear();
            commandInputCursor = 0;
            historyDraft = "";
            historyStartedAtPrompt = true;
            NavigateHistory(up: true);
            e.Handled = true;
            return;
        }

        if (isEditingDuckyCommand)
        {
            HandleDuckyCommandKey(e);
            return;
        }

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

//        if (ctrl && e.Key is Key.Left or Key.Right)
//        {
//            MoveSelection(e.Key, shift, byWord: true);
//            e.Handled = true;
//            return;
//        }

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
            Key.Left => e.KeyModifiers.HasFlag(KeyModifiers.Control)
                ? "\x1B[1;5D"
                : "\x1B[D",

            Key.Right => e.KeyModifiers.HasFlag(KeyModifiers.Control)
                ? "\x1B[1;5C"
                : "\x1B[C",
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