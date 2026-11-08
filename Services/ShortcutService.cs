using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Input;

namespace ducky;

internal sealed record ShortcutDefinition(
    string Name,
    string ConfigKey,
    Func<Config, string> GetValue,
    string DefaultValue);

internal static class ShortcutService
{
    private static readonly Config Defaults = new();

    public static IReadOnlyList<ShortcutDefinition> Definitions { get; } =
        new List<ShortcutDefinition>
        {
            Define("Copy selection", "copy", config => config.Copy, config => config.Copy),
            Define("Paste", "paste", config => config.Paste, config => config.Paste),
            Define("Increase font size", "increse-font-size", config => config.IncreaseFontSize, config => config.IncreaseFontSize),
            Define("Decrease font size", "decrese-font-size", config => config.DecreseFontSize, config => config.DecreseFontSize),
            Define("Split terminal vertically", "terminal-vertical-split", config => config.TerminalVerticalSplit, config => config.TerminalVerticalSplit),
            Define("Split terminal horizontally", "terminal-horizontal-split", config => config.TerminalHorizontalSplit, config => config.TerminalHorizontalSplit),
            Define("Focus next pane", "terminal-next-split", config => config.TerminalNextSplit, config => config.TerminalNextSplit),
            Define("Focus previous pane", "terminal-previous-split", config => config.TerminalPreviousSplit, config => config.TerminalPreviousSplit),
            Define("Close active pane", "terminal-close-split", config => config.TerminalCloseSplit, config => config.TerminalCloseSplit),
            Define("Choose shell for active pane", "shell-picker", config => config.ShellPicker, config => config.ShellPicker),
            Define("Next workspace", "terminal-next-workspace", config => config.TerminalNextWorkspace, config => config.TerminalNextWorkspace),
            Define("Previous workspace", "terminal-previous-workspace", config => config.TerminalPreviousWorkspace, config => config.TerminalPreviousWorkspace),
            Define("Open temporary terminal", "temporary-terminal", config => config.TemporaryTerminal, config => config.TemporaryTerminal),
            Define("Workspace 1", "workspace-1", config => config.Workspace1, config => config.Workspace1),
            Define("Workspace 2", "workspace-2", config => config.Workspace2, config => config.Workspace2),
            Define("Workspace 3", "workspace-3", config => config.Workspace3, config => config.Workspace3),
            Define("Workspace 4", "workspace-4", config => config.Workspace4, config => config.Workspace4),
            Define("Workspace 5", "workspace-5", config => config.Workspace5, config => config.Workspace5),
            Define("Workspace 6", "workspace-6", config => config.Workspace6, config => config.Workspace6),
            Define("Workspace 7", "workspace-7", config => config.Workspace7, config => config.Workspace7),
            Define("Workspace 8", "workspace-8", config => config.Workspace8, config => config.Workspace8),
            Define("Workspace 9", "workspace-9", config => config.Workspace9, config => config.Workspace9),
            Define("Workspace 10", "workspace-10", config => config.Workspace10, config => config.Workspace10),
            Define("Open File menu", "file-menu", config => config.FileMenu, config => config.FileMenu),
            Define("Open Ducky Manager", "ducky-manager", config => config.DuckyManager, config => config.DuckyManager),
            Define("Open timer", "timer-window", config => config.TimerWindow, config => config.TimerWindow),
            Define("Close timer", "timer-close", config => config.TimerClose, config => config.TimerClose),
            Define("Start timer", "timer-start", config => config.TimerStart, config => config.TimerStart),
            Define("Start countdown", "countdown-start", config => config.CountdownStart, config => config.CountdownStart),
            Define("Pause or resume timer", "timer-pause", config => config.TimerPause, config => config.TimerPause),
            Define("Reset timer", "timer-reset", config => config.TimerReset, config => config.TimerReset)
        };

    public static void SaveBinding(ShortcutDefinition definition, string value)
    {
        string path = DuckyPaths.ConfigFilePath;
        List<string> lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();
        bool updated = false;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            int separator = line.IndexOf('=');
            if (separator < 0 ||
                !string.Equals(
                    line[..separator].Trim(),
                    definition.ConfigKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines[index] = definition.ConfigKey + " = " + value;
            updated = true;
        }

        if (!updated)
        {
            if (lines.Count > 0 && !string.IsNullOrEmpty(lines[^1]))
                lines.Add("");

            lines.Add(definition.ConfigKey + " = " + value);
        }

        File.WriteAllLines(path, lines);
    }

    public static string FormatBinding(Key key, KeyModifiers modifiers)
    {
        List<string> parts = new();

        if (modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("alt");
        if (modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("shift");
        if (modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("meta");

        string keyName = key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(),
            Key.Add => "add",
            Key.OemPlus => parts.Count == 0 ? "add" : "+",
            Key.Subtract or Key.OemMinus => "-",
            Key.Space => "space",
            _ => key.ToString().ToLowerInvariant()
        };

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static ShortcutDefinition Define(
        string name,
        string configKey,
        Func<Config, string> getValue,
        Func<Config, string> getDefault) =>
        new(name, configKey, getValue, getDefault(Defaults));
}