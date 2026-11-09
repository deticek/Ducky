using System;

namespace ducky;

public sealed class CustomCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? TerminalCommand { get; set; }
    public string Trigger { get; set; } = CustomCommandTriggers.OnCommand;
    public string Shell { get; set; } = CustomCommandShells.Default;
    public string CustomShellPath { get; set; } = "";
    public string Script { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public int Order { get; set; }
}

public static class CustomCommandTriggers
{
    public const string OnCommand = "On command";
    public const string OnStartup = "On Ducky startup";
    public const string OnExit = "On Ducky exit";
    public const string OnTerminalOpen = "On terminal open";
    public const string OnPaneOpen = "On pane open";
}

public static class CustomCommandShells
{
    public const string Default = "Default shell";
    public const string Cmd = "CMD";
    public const string PowerShell = "PowerShell";
    public const string Custom = "Custom shell";
}