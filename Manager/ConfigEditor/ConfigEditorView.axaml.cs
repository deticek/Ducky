using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

namespace ducky;

public partial class ConfigView : UserControl
{
    private static readonly Config Defaults = new();
    private static readonly IReadOnlyList<ConfigSettingDefinition> Definitions =
    [
        Define("Appearance", "Background color", "BackgroundColor", "background-color"),
        Define("Appearance", "Text color", "TextColor", "text-color"),
        Define("Appearance", "Font family", "FontFamily", "font-family"),
        Define("Appearance", "Font size", "FontSize", "font-size"),
        Define("Appearance", "Show navbar", "ShowNavbar", "show-navbar"),
        Define("Shell", "Default shell", "Shell", "shell"),
        Define("Shell", "Shell path", "ShellPath", "shell-path"),
        Define("Shell", "Shell arguments", "ShellArgs", "shell-args"),
        Define("Workspaces", "Maximum workspaces", "WorkspaceMax", "workspace-max"),
        Define("Terminal", "Always open in temporary terminal", "AlwaysOpenInTemporaryTerminal", "always-open-in-temporary-terminal"),
        Define("Timer", "Default countdown time", "DefaultCountdownTime", "default-countdown-time"),
        Define("Terminal commands", "Enable /timer-start", "TimerStartCommandEnabled", "timer-start-command-enabled"),
        Define("Terminal commands", "Enable /timer-countdown", "TimerCountdownCommandEnabled", "timer-countdown-command-enabled"),
        Define("Terminal commands", "Enable /timer-pause", "TimerPauseCommandEnabled", "timer-pause-command-enabled"),
        Define("Terminal commands", "Enable /timer-reset", "TimerResetCommandEnabled", "timer-reset-command-enabled"),
        Define("Terminal commands", "Enable /timer-close", "TimerCloseCommandEnabled", "timer-close-command-enabled"),
        Define("Integrations", "Discord bot integration", "DiscordBotIntegration", "discord-bot-integration")
    ];

    public ConfigView()
    {
        InitializeComponent();
        RefreshSettings();
    }

    private void RefreshSettings()
    {
        Config config = ConfigReder.GetConfig();
        ConfigSectionsPanel.Children.Clear();

        foreach (IGrouping<string, ConfigSettingDefinition> section in
                 Definitions.GroupBy(definition => definition.Section))
        {
            StackPanel rows = new() { Spacing = 8 };
            foreach (ConfigSettingDefinition definition in section)
                rows.Children.Add(CreateSettingRow(definition, config));

            ConfigSectionsPanel.Children.Add(new Border
            {
                Classes = { "config-card" },
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = section.Key,
                            FontSize = 17,
                            FontWeight = FontWeight.SemiBold
                        },
                        rows
                    }
                }
            });
        }
    }

    private Control CreateSettingRow(
        ConfigSettingDefinition definition,
        Config config)
    {
        TextBox valueInput = new()
        {
            Text = ReadValue(definition, config),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Button resetButton = new() { Content = "Reset" };
        Button saveButton = new()
        {
            Content = "Save",
            Classes = { "primary" }
        };
        resetButton.Click += (_, _) => SaveValue(
            definition,
            definition.GetDefaultValue(),
            valueInput);
        saveButton.Click += (_, _) => SaveValue(
            definition,
            valueInput.Text ?? "",
            valueInput);

        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("180,*,Auto,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        row.Children.Add(new TextBlock
        {
            Text = definition.Name,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Grid.SetColumn(valueInput, 1);
        row.Children.Add(valueInput);
        Grid.SetColumn(resetButton, 2);
        row.Children.Add(resetButton);
        Grid.SetColumn(saveButton, 3);
        row.Children.Add(saveButton);
        return row;
    }

    private void SaveValue(
        ConfigSettingDefinition definition,
        string value,
        TextBox valueInput)
    {
        string? validationError = ValidateValue(definition, value);
        if (validationError is not null)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#F1B7B7"));
            StatusText.Text = validationError;
            return;
        }

        try
        {
            SaveConfigValue(definition, value);
            MainWindow.ReloadConfigFromDisk();
            valueInput.Text = value;
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#9AD9AE"));
            StatusText.Text = definition.Name + " saved.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not save configuration value: " + ex);
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#F1B7B7"));
            StatusText.Text = "Could not save " + definition.Name + ": " + ex.Message;
        }
    }

    private static void SaveConfigValue(
        ConfigSettingDefinition definition,
        string value)
    {
        string serializedValue = SerializeValue(definition, value);
        string path = DuckyPaths.ConfigFilePath;
        List<string> lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();
        HashSet<string> matchingKeys = new(
            definition.AllKeys,
            StringComparer.OrdinalIgnoreCase);
        bool updated = false;

        for (int index = 0; index < lines.Count; index++)
        {
            int separator = lines[index].IndexOf('=');
            if (separator < 0 ||
                !matchingKeys.Contains(lines[index][..separator].Trim()))
            {
                continue;
            }

            lines[index] = definition.ConfigKey + " = " + serializedValue;
            updated = true;
        }

        if (!updated)
        {
            if (lines.Count > 0 && !string.IsNullOrEmpty(lines[^1]))
                lines.Add("");
            lines.Add(definition.ConfigKey + " = " + serializedValue);
        }

        string temporaryPath = path + ".tmp";
        File.WriteAllLines(temporaryPath, lines);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string SerializeValue(
        ConfigSettingDefinition definition,
        string value)
    {
        if (definition.PropertyName is "Shell" or "ShellPath" &&
            value.Any(char.IsWhiteSpace) &&
            !(value.StartsWith('"') && value.EndsWith('"')))
        {
            return '"' + value + '"';
        }

        return value;
    }

    private static string? ValidateValue(
        ConfigSettingDefinition definition,
        string value)
    {
        if (value.Contains('\r') || value.Contains('\n'))
            return definition.Name + " must be a single-line value.";

        if (definition.PropertyName is "BackgroundColor" or "TextColor" &&
            !Color.TryParse(value, out _))
        {
            return definition.Name + " must be a valid color.";
        }

        if (definition.PropertyName == "FontFamily" &&
            string.IsNullOrWhiteSpace(value))
        {
            return "Font family cannot be empty.";
        }

        if (definition.PropertyName == "FontSize" &&
            (!double.TryParse(
                 value,
                 NumberStyles.Float,
                 CultureInfo.InvariantCulture,
                 out double fontSize) ||
             !double.IsFinite(fontSize) ||
             fontSize < 6 ||
             fontSize > 72))
        {
            return "Font size must be a number from 6 to 72.";
        }

        if (definition.PropertyName == "WorkspaceMax" &&
            (!int.TryParse(
                 value,
                 NumberStyles.Integer,
                 CultureInfo.InvariantCulture,
                 out int workspaceMax) ||
             workspaceMax < 1 ||
             workspaceMax > 100))
        {
            return "Maximum workspaces must be an integer from 1 to 100.";
        }

        if (definition.PropertyName == "DefaultCountdownTime" &&
            !IsValidCountdown(value))
        {
            return "Countdown time must be seconds, mm:ss, or hh:mm:ss.";
        }

        if (IsBooleanProperty(definition.PropertyName) &&
            !IsValidBoolean(value))
        {
            return definition.Name + " must be true/false, on/off, yes/no, or 1/0.";
        }

        return null;
    }

    private static bool IsBooleanProperty(string propertyName) =>
        propertyName is
            "ShowNavbar" or
            "AlwaysOpenInTemporaryTerminal" or
            "TimerStartCommandEnabled" or
            "TimerCountdownCommandEnabled" or
            "TimerPauseCommandEnabled" or
            "TimerResetCommandEnabled" or
            "TimerCloseCommandEnabled" or
            "DiscordBotIntegration";

    private static bool IsValidBoolean(string value) =>
        bool.TryParse(value, out _) ||
        value.Trim().ToLowerInvariant() is
            "1" or "0" or "yes" or "no" or "on" or "off" or "show";

    private static bool IsValidCountdown(string value)
    {
        string[] parts = value.Trim().Split(':');
        if (parts.Length == 1)
        {
            return int.TryParse(
                       parts[0],
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out int seconds) &&
                   seconds >= 0;
        }

        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) ||
                minutes < 0 ||
                seconds is < 0 or > 59)
            {
                return false;
            }

            try
            {
                _ = new TimeSpan(0, minutes, seconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutesPart) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int secondsPart) ||
            hours < 0 ||
            minutesPart is < 0 or > 59 ||
            secondsPart is < 0 or > 59)
        {
            return false;
        }

        try
        {
            _ = new TimeSpan(hours, minutesPart, secondsPart);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string ReadValue(
        ConfigSettingDefinition definition,
        Config config)
    {
        object? value = definition.Property.GetValue(config);
        return FormatValue(value);
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        bool boolean => boolean ? "true" : "false",
        double number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        TimeSpan time => FormatCountdown(time),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static string FormatCountdown(TimeSpan value) =>
        $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private static ConfigSettingDefinition Define(
        string section,
        string name,
        string propertyName,
        string configKey,
        params string[] aliases)
    {
        System.Reflection.PropertyInfo property =
            typeof(Config).GetProperty(propertyName) ??
            throw new InvalidOperationException(
                $"Config property '{propertyName}' was not found.");
        return new ConfigSettingDefinition(
            section,
            name,
            propertyName,
            configKey,
            property,
            aliases);
    }

    private sealed record ConfigSettingDefinition(
        string Section,
        string Name,
        string PropertyName,
        string ConfigKey,
        System.Reflection.PropertyInfo Property,
        IReadOnlyList<string> Aliases)
    {
        public IEnumerable<string> AllKeys => Aliases.Prepend(ConfigKey);

        public string GetDefaultValue() =>
            FormatValue(Property.GetValue(Defaults));
    }
}
