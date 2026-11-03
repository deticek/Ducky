
using System;
using System.Collections.Generic;
using System.Linq;

namespace ducky;

public class KeyManager
{
    public static string? GetFunction(string key, Config config)
    {
        
        key = key.ToLowerInvariant();

        if (MatchesBinding(key, config.LogTest)) return "logtest";
        if (MatchesBinding(key, config.Copy)) return "copy";
        if (MatchesBinding(key, config.Paste)) return "paste";
        if (MatchesBinding(key, config.IncreaseFontSize)) return "incresefontsize";
        if (MatchesBinding(key, config.DecreseFontSize)) return "descresefontsize";
        if (MatchesBinding(key, config.TerminalVerticalSplit)) return "verticalsplit";
        if (MatchesBinding(key, config.TerminalHorizontalSplit)) return "horizontalsplit";
        if (MatchesBinding(key, config.TerminalNextSplit)) return "nextsplit";
        if (MatchesBinding(key, config.TerminalPreviousSplit)) return "previussplit";
        if (MatchesBinding(key, config.TerminalCloseSplit)) return "closesplit";
        if (MatchesBinding(key, config.TerminalNextWorkspace)) return "nextws";
        if (MatchesBinding(key, config.TerminalPreviousWorkspace)) return "previusws";
        if (MatchesBinding(key, config.TemporaryTerminal)) return "temporaryterminal";
        if (MatchesBinding(key, config.Workspace1)) return "workspace1";
        if (MatchesBinding(key, config.Workspace2)) return "workspace2";
        if (MatchesBinding(key, config.Workspace3)) return "workspace3";
        if (MatchesBinding(key, config.Workspace4)) return "workspace4";
        if (MatchesBinding(key, config.Workspace5)) return "workspace5";
        if (MatchesBinding(key, config.Workspace6)) return "workspace6";
        if (MatchesBinding(key, config.Workspace7)) return "workspace7";
        if (MatchesBinding(key, config.Workspace8)) return "workspace8";
        if (MatchesBinding(key, config.Workspace9)) return "workspace9";
        if (MatchesBinding(key, config.Workspace10)) return "workspace10";
        if (MatchesBinding(key, config.HelpWindow)) return "helpwindow";
        if (MatchesBinding(key, config.DuckyManager)) return "duckymanager";
        if (MatchesBinding(key, config.TimerWindow)) return "timerwindow";
        if (MatchesBinding(key, config.TimerClose)) return "timerclose";
        if (MatchesBinding(key, config.TimerStart)) return "timerstart";
        if(MatchesBinding(key, config.CountdownStart)) return "countdownstart";
        if (MatchesBinding(key, config.TimerPause)) return "timerpause";
        if (MatchesBinding(key, config.TimerReset)) return "timerreset";
        return "null";
    }

    public static string? GetTimerFunction(string key, Config config)
    {
        key = key.ToLowerInvariant();
        
        if(MatchesBinding(key, config.TimerWindow)) return "timerwindow";
        if(MatchesBinding(key, config.TimerClose)) return "timerclose";
        if(MatchesBinding(key, config.TimerStart)) return "timerstart";
        if(MatchesBinding(key, config.TimerPause)) return "timerpause";
        if(MatchesBinding(key, config.TimerReset)) return "timerreset";
        if(MatchesBinding(key, config.CountdownStart)) return "countdownstart";

        return null;
    }

    private static bool MatchesBinding(string pressed, string configured)
    {
        string[] pressedParts = pressed.Split('+');
        string[] configuredParts = configured.Split('+');

        // Zadnja tipka mora biti enaka, npr. y.
        if (pressedParts.Length == 0 || configuredParts.Length == 0)
            return false;

        if (pressedParts[^1] != configuredParts[^1])
            return false;
        
        var configuredModifiers = new HashSet<string>(
            configuredParts[..^1].Select(NormalizeModifier));

        var pressedModifiers = new HashSet<string>(
            pressedParts[..^1].Select(modifier =>
            {
                // Splošni Ctrl v configu sprejme levo ali desno.
                if (configuredModifiers.Contains("ctrl") &&
                    (modifier == "leftctrl" || modifier == "rightctrl"))
                    return "ctrl";

                if ((configuredModifiers.Contains("shift") ||
                     configuredModifiers.Contains("sift")) &&
                    (modifier == "leftshift" || modifier == "rightshift"))
                    return configuredModifiers.Contains("sift") ? "sift" : "shift";

                if (configuredModifiers.Contains("alt") &&
                    (modifier == "leftalt" || modifier == "rightalt"))
                    return "alt";

                if (configuredModifiers.Contains("meta") &&
                    (modifier == "leftmeta" || modifier == "rightmeta"))
                    return "meta";

                return NormalizeModifier(modifier);
            }));

        return pressedModifiers.SetEquals(configuredModifiers);
    }

     
    private static string NormalizeModifier(string modifier)
    {
        return modifier switch
        {
            // Ctrl: loči levo in desno.
            "leftctrl" => "leftctrl",
            "rightctrl" => "rightctrl",
            "ctrl" or "control" => "ctrl",

            // Shift: config uporablja "sift".
            "leftshift" => "leftshift",
            "rightshift" => "rightshift",
            "shift" or "sift" => "shift",

            // Alt: loči levo in desno.
            "leftalt" => "leftalt",
            "rightalt" => "rightalt",
            "alt" => "alt",

            "leftmeta" => "leftmeta",
            "rightmeta" => "rightmeta",
            "meta" => "meta",

            _ => modifier
        };
    }

}
