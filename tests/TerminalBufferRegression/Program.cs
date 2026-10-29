using System;
using System.IO;
using Avalonia.Media;
using test;

var terminal = new VtTerminalBuffer(
    12,
    4,
    Colors.White,
    Colors.Black
);

terminal.Feed("\x1b[?1049h\x1b[2J\x1b[H");
terminal.Feed(
    "\x1b[1;3H0" +
    "\x1b[2;3H1" +
    "\x1b[3;3H2" +
    "\x1b[4;3H3"
);

AssertCell(terminal, 0, 0, " ");
AssertCell(terminal, 1, 0, " ");
AssertCell(terminal, 2, 0, "0");
AssertCell(terminal, 1, 1, " ");
AssertCell(terminal, 2, 1, "1");
AssertCell(terminal, 1, 2, " ");
AssertCell(terminal, 2, 2, "2");
AssertCell(terminal, 1, 3, " ");
AssertCell(terminal, 2, 3, "3");

terminal.Feed("\x1b[2;1H\x1b[K\x1b[2;3H1");

AssertCell(terminal, 1, 1, " ");
AssertCell(terminal, 2, 1, "1");

var wrappingTerminal = new VtTerminalBuffer(
    4,
    2,
    Colors.White,
    Colors.Black
);
wrappingTerminal.Feed("abcd\x1b[31mX");

AssertCell(wrappingTerminal, 3, 0, "d");
AssertCell(wrappingTerminal, 0, 1, "X");

var pasteModeTerminal = new VtTerminalBuffer(
    4,
    2,
    Colors.White,
    Colors.Black
);
pasteModeTerminal.Feed("\x1b[?2004h");
Assert(pasteModeTerminal.BracketedPasteEnabled, "Bracketed paste should enable.");
pasteModeTerminal.Feed("\x1b[?2004l");
Assert(!pasteModeTerminal.BracketedPasteEnabled, "Bracketed paste should disable.");

var recoloredTerminal = new VtTerminalBuffer(4, 2, Colors.White, Colors.Black);
recoloredTerminal.Feed("x");
recoloredTerminal.UpdateDefaultColors(Colors.Yellow, Colors.Blue);
Assert(
    recoloredTerminal.GetCell(0, 0).Foreground == Colors.Yellow &&
    recoloredTerminal.GetCell(0, 0).Background == Colors.Blue,
    "Changing default colors should update existing default-colored cells.");

var sharedFontConfig = new Config { FontSize = 16 };
Config firstTerminalConfig = sharedFontConfig.Clone();
Config secondTerminalConfig = sharedFontConfig.Clone();
firstTerminalConfig.FontSize++;
Assert(sharedFontConfig.FontSize == 16 && secondTerminalConfig.FontSize == 16,
    "Terminal font size changes should not mutate other views through shared config.");

string configPath = Path.GetTempFileName();
try
{
    File.WriteAllText(
        configPath,
        string.Join(
            Environment.NewLine,
            "shell = " + '"' + @"C:\Tools\nu.exe" + '"',
            "shell-path = " + '"' + @"C:\Tools\nu.exe" + '"',
            "shell-args = --no-config --stdin",
            "workspace-max = 5",
            "temporary-terminal = ctrl+shift+n",
            "always-open-in-temporary-terminal = true")
    );

    Config shellConfig = ConfigReder.GetConfig(configPath);
    Assert(shellConfig.Shell == "C:\\Tools\\nu.exe", "Configured shell should be parsed.");
    Assert(shellConfig.ShellPath == @"C:\Tools\nu.exe", "Configured shell path should be parsed.");
    Assert(shellConfig.ShellArgs == "--no-config --stdin", "Shell arguments should be parsed.");
    Assert(shellConfig.WorkspaceMax == 5, "Workspace maximum should be parsed.");
    Assert(shellConfig.TemporaryTerminal == "ctrl+shift+n",
        "Temporary terminal shortcut should be parsed.");
    Assert(shellConfig.AlwaysOpenInTemporaryTerminal,
        "Always-open temporary terminal setting should be parsed.");
    Assert(
        KeyManager.GetFunction("leftctrl+leftshift+n", shellConfig) == "temporaryterminal",
        "Configured temporary terminal shortcut should resolve.");

    File.WriteAllText(configPath, "workspace-max = 101");
    Config invalidWorkspaceConfig = ConfigReder.GetConfig(configPath);
    Assert(
        invalidWorkspaceConfig.WorkspaceMax == 20,
        "Workspace maximum above 100 should use the default.");

    File.WriteAllText(configPath, "workspace-max = 1");
    Config minimumWorkspaceConfig = ConfigReder.GetConfig(configPath);
    Assert(
        minimumWorkspaceConfig.WorkspaceMax == 1,
        "Workspace maximum should allow a single workspace.");

    File.WriteAllText(configPath, "workspace-max = 46");
    Config largerWorkspaceConfig = ConfigReder.GetConfig(configPath);
    Assert(
        largerWorkspaceConfig.WorkspaceMax == 46,
        "Workspace maximum values above the original startup cap should parse.");

    var workspaceState = new WorkspaceSessionState
    {
        ActiveWorkspaceIndex = 1,
        Workspaces =
        [
            new WorkspaceLayoutState
            {
                ActivePaneIndex = 1,
                Root = new WorkspacePaneState
                {
                    Orientation = "Vertical",
                    First = new WorkspacePaneState
                    {
                        WorkingDirectory = @"C:\Projects\first"
                    },
                    Second = new WorkspacePaneState
                    {
                        Orientation = "Horizontal",
                        First = new WorkspacePaneState
                        {
                            WorkingDirectory = @"D:\Projects\second"
                        },
                        Second = new WorkspacePaneState
                        {
                            WorkingDirectory = @"E:\Projects\third"
                        }
                    }
                }
            }
        ]
    };
    string workspaceJson = WorkspaceStore.Serialize(workspaceState);
    WorkspaceSessionState restoredWorkspaceState =
        WorkspaceStore.Deserialize(workspaceJson)
        ?? throw new InvalidOperationException("Workspace state should deserialize.");
    Assert(restoredWorkspaceState.ActiveWorkspaceIndex == 1,
        "Active workspace should be persisted.");
    Assert(restoredWorkspaceState.Workspaces[0].ActivePaneIndex == 1,
        "Active pane should be persisted.");
    Assert(
        restoredWorkspaceState.Workspaces[0].Root?.Second?.First?.WorkingDirectory ==
        @"D:\Projects\second",
        "Each split pane working directory should be persisted.");

    string cmdEnvironment = ConPtySession.BuildEnvironmentBlock(
        configureCmdPrompt: true);
    Assert(
        cmdEnvironment.Contains("PROMPT=$E]9;9;$P$E\\$P$G", StringComparison.OrdinalIgnoreCase),
        "CMD should report its working directory through its environment prompt.");
    Assert(
        cmdEnvironment.EndsWith("\0\0", StringComparison.Ordinal),
        "Child environment block should be double-null terminated.");

    string linuxConfig = DuckyPaths.ConfigureLinuxShell(
        "# shell = powershell.exe\r\nshell = cmd.exe\r\nworkspace-max = 20\r\n");
    Assert(
        linuxConfig.Contains(
            "shell = " + DuckyPaths.GetLinuxDefaultShell(),
            StringComparison.Ordinal),
        "Linux user config should use an available Unix shell instead of cmd.exe.");
    Assert(
        !linuxConfig.Contains("shell = cmd.exe", StringComparison.OrdinalIgnoreCase),
        "Linux user config should not activate cmd.exe.");
}
finally
{
    File.Delete(configPath);
}

Console.WriteLine("Terminal buffer regression checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertCell(
    VtTerminalBuffer terminal,
    int column,
    int row,
    string expected)
{
    string actual = terminal.GetCell(column, row).Character;

    if (actual != expected)
    {
        throw new InvalidOperationException(
            $"Cell ({column},{row}): expected '{expected}', got '{actual}'.");
    }
}
