using System;
using System.Globalization;
using System.IO;

namespace ducky;

public class ConfigReder
{

    private static string ConvertKeyBiding(string input)
    {
        input = input.ToLowerInvariant().Replace(" ", "");

        if (input == "null")
            return "null";

        // Posebna obravnava plusa in minusa na koncu kombinacije
        if (input.EndsWith("++"))
            input = input[..^2] + "+add";
        else if (input.EndsWith("+-"))
            input = input[..^2] + "+-";

        string[] parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries);

        string keys = "";

        foreach (string part in parts)
        {
            if (part is "leftctrl" or "leftcontrol")
                keys += "leftctrl+";
            else if (part == "rightctrl" || part == "rightcontrol")
                keys += "rightctrl+";
            else if (part is "ctrl" or "control")
                keys += "ctrl+";

            else if (part == "leftshift")
                keys += "leftshift+";
            else if (part == "rightshift")
                keys += "rightshift+";
            else if (part == "shift")
                keys += "shift+";

            else if (part == "leftalt")
                keys += "leftalt+";
            else if (part == "rightalt" || part == "altgr")
                keys += "rightalt+";
            else if (part == "alt")
                keys += "alt+";

            else if (part is "meta" or "super" or "windows" or "win")
                keys += "meta+";

            else if (part is "add" or "oemadd" or "plus")
                keys += "add+";
            else if (part is "subtract" or "oemminus" or "minus")
                keys += "subtract+";

            else
                keys += part + "+";
        }

        return keys.TrimEnd('+');
    }

    private static TimeSpan ConvertCountdownTime(string value)
    {
        value = value.Trim();

        if(int.TryParse(value, out int seconds))
        {
            if(seconds < 0)
                return TimeSpan.FromMinutes(25);

            return TimeSpan.FromSeconds(seconds);
        }

        string[] parts = value.Split(':');

        if(parts.Length == 2)
        {
            if(int.TryParse(parts[0], out int minutes) &&
               int.TryParse(parts[1], out int parsedSeconds))
            {
                if(minutes < 0 || parsedSeconds < 0 || parsedSeconds > 59)
                    return TimeSpan.FromMinutes(25);

                return new TimeSpan(0, minutes, parsedSeconds);
            }
        }

        if(parts.Length == 3)
        {
            if(int.TryParse(parts[0], out int hours) &&
               int.TryParse(parts[1], out int minutes) &&
               int.TryParse(parts[2], out int parsedSeconds))
            {
                if(hours < 0 || minutes < 0 || minutes > 59 ||
                   parsedSeconds < 0 || parsedSeconds > 59)
                    return TimeSpan.FromMinutes(25);
            
                return new TimeSpan(hours, minutes, parsedSeconds);
            }
        }

        return TimeSpan.FromMinutes(25);
    }

    public static Config GetConfig(string? configPath = null)
    {
        string path = configPath ?? DuckyPaths.ConfigFilePath;

        Config conf = new Config();

        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Config file not found: {path}");
                return conf;
            }

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                int separator = line.IndexOf('=');

                if (separator < 0)
                    continue;

                string key = line[..separator].Trim().ToLowerInvariant();
                string value = line[(separator + 1)..].Trim();

                switch (key)
                {
                    case "background-color":
                        conf.BackgroundColor = value;
                        break;

                    case "text-color":
                        conf.TextColor = value;
                        break;

                    case "font-family":
                        conf.FontFamily = value;
                        break;

                    case "font-size":
                        if (double.TryParse(
                            value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double fontSize)
                            && fontSize >= 6
                            && fontSize <= 72)
                        {
                            conf.FontSize = fontSize;
                        }
                        break;
                    case "increse-font-size":
                        conf.IncreaseFontSize = ConvertKeyBiding(value.ToLower());
                        break;
                    case "decrese-font-size":
                        conf.DecreseFontSize = ConvertKeyBiding(value.ToLower());
                        break;
                    case "show-navbar":
                            if(value is "true" or "show" or "1"){
                                conf.ShowNavbar = true;
                            }else{
                                conf.ShowNavbar = false;
                            }
                        break;
                    case "shell":
                        conf.Shell = Unquote(value);
                        break;

                    case "shell-path":
                        conf.ShellPath = Unquote(value);
                        break;

                    case "shell-args":
                        conf.ShellArgs = value;
                        break;

                    case "workspace-max":
                        if (int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int workspaceMax)
                            && workspaceMax >= 1
                            && workspaceMax <= 100)
                        {
                            conf.WorkspaceMax = workspaceMax;
                        }
                        break;
                    case "logtest":
                        conf.LogTest = ConvertKeyBiding(value.ToLower());
                        break;
                    case "copy":
                        conf.Copy = ConvertKeyBiding(value.ToLower());
                        break;
                    case "paste":
                        conf.Paste = ConvertKeyBiding(value.ToLower());
                        break;
                    case "termina-vertical-split":
                    case "terminal-vertical-split":
                        conf.TerminalVerticalSplit = ConvertKeyBiding(value.ToLower());
                        break;
                    case "terminal-horizontal-split":
                        conf.TerminalHorizontalSplit = ConvertKeyBiding(value.ToLower());
                        break;
                    case "terminal-next-split":
                        conf.TerminalNextSplit = ConvertKeyBiding(value.ToLower());
                        break;
                    case "terminal-previous-split":
                        conf.TerminalPreviousSplit = ConvertKeyBiding(value.ToLower());
                        break;
                    case "terminal-close-split":
                        conf.TerminalCloseSplit = ConvertKeyBiding(value.ToLower());
                        break;
                    case "shell-picker":
                        conf.ShellPicker = ConvertKeyBiding(value.ToLowerInvariant());
                        break;
                    case "terminal-next-workspace":
                        conf.TerminalNextWorkspace = ConvertKeyBiding(value.ToLower());
                        break;
                    case "terminal-previus-workspace":
                    case "terminal-previous-workspace":
                        conf.TerminalPreviousWorkspace = ConvertKeyBiding(value.ToLower());
                        break;
                    case "temporary-terminal":
                        conf.TemporaryTerminal = ConvertKeyBiding(value.ToLowerInvariant());
                        break;
                    case "always-open-in-temporary-terminal":
                        if (bool.TryParse(value, out bool alwaysOpenTemporary))
                            conf.AlwaysOpenInTemporaryTerminal = alwaysOpenTemporary;
                        else if (value is "1" or "yes" or "on")
                            conf.AlwaysOpenInTemporaryTerminal = true;
                        else if (value is "0" or "no" or "off")
                            conf.AlwaysOpenInTemporaryTerminal = false;
                        break;
                    case "workspace-1":
                        conf.Workspace1 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-2":
                        conf.Workspace2 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-3":
                        conf.Workspace3 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-4":
                        conf.Workspace4 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-5":
                        conf.Workspace5 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-6":
                        conf.Workspace6 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-7":
                        conf.Workspace7 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-8":
                        conf.Workspace8 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-9":
                        conf.Workspace9 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "workspace-10":
                        conf.Workspace10 = ConvertKeyBiding(value.ToLower());
                        break;
                    case "ducky-manager":
                        conf.DuckyManager = ConvertKeyBiding(value.ToLower());
                        break;
                    case "file-menu":
                        conf.FileMenu = ConvertKeyBiding(value.ToLowerInvariant());
                        break;
                    case "timer-window":
                        conf.TimerWindow = ConvertKeyBiding(value.ToLower());
                        break;
                    case "timer-start":
                        conf.TimerStart = ConvertKeyBiding(value);
                        break;

                    case "timer-pause":
                        conf.TimerPause = ConvertKeyBiding(value);
                        break;

                    case "timer-reset":
                        conf.TimerReset = ConvertKeyBiding(value);
                        break;
                    case "countdown-start":
                        conf.CountdownStart = ConvertKeyBiding(value);
                        break;
                    case "default-countdown-time":
                        conf.DefaultCountdownTime = ConvertCountdownTime(value);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Could not read config: {e.Message}");
        }

        return conf;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            value[0] == '"' &&
            value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }
}