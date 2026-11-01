using System;

namespace ducky;

public class Config
{
    public string BackgroundColor { get; set; } = "#11111B";
    public string TextColor { get; set; } = "#FFFFFF";

    public string FontFamily { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 15;
    public string IncreaseFontSize { get; set; } = "ctrl++";
    public string DecreseFontSize { get; set; } = "ctrl+-";

    public bool ShowNavbar { get; set; } = true;

    public string Shell { get; set; } = GetDefaultShell();
    public string ShellPath { get; set; } = "";
    public string ShellArgs { get; set; } = "";
    public int WorkspaceMax { get; set; } = 20;
    public string Copy  { get; set; } = "ctrl+C";
    public string Paste  { get; set; } = "ctrl+v";
    public string TerminalVerticalSplit  { get; set; } = "ctrl+t";
    public string TerminalHorizontalSplit  { get; set; } = "atl+h";
    public string TerminalNextSplit  { get; set; } = "ctrl+tab";
    public string TerminalCloseSplit { get; set; }= "ctrl+w";


    public string TerminalPreviousSplit  { get; set; } = "ctrl+sift+tab";
    public string TerminalNextWorkspace  { get; set; } = "alt++";
    public string TerminalPreviousWorkspace  { get; set; } = "alt+-";
    public string TemporaryTerminal { get; set; } = "ctrl+shift+n";
    public bool AlwaysOpenInTemporaryTerminal { get; set; }

    public string Workspace1 { get; set; } = "alt+1";
    public string Workspace2 { get; set; } = "alt+2";
    public string Workspace3 { get; set; } = "alt+3";
    public string Workspace4 { get; set; } = "alt+4";
    public string Workspace5 { get; set; } = "alt+5";
    public string Workspace6 { get; set; } = "alt+6";
    public string Workspace7 { get; set; } = "alt+7";
    public string Workspace8 { get; set; } = "alt+8";
    public string Workspace9 { get; set; } = "alt+9";
    public string Workspace10 { get; set; } = "alt+0";

    public string HelpWindow {get; set; } = "f1";
    public int HelpWindowWidth { get; set; } = 47;

    // keybinds
    public string LogTest { get; set; } = "Alt+Control+Sift+Y";

    public Config()
    {
    }

    public Config(string bg, string tc)
    {
        BackgroundColor = bg;
        TextColor = tc;
    }

    public Config Clone() => (Config)MemberwiseClone();

    private static string GetDefaultShell()
    {
        if (OperatingSystem.IsWindows())
            return "cmd.exe";

        string? userShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(userShell) &&
            System.IO.Path.IsPathRooted(userShell) &&
            System.IO.File.Exists(userShell))
            return userShell;

        return OperatingSystem.IsLinux() && System.IO.File.Exists("/bin/bash")
            ? "/bin/bash"
            : "sh";
    }
}