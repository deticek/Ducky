
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ducky;

internal static class ThemeStore
{
    public static string ThemesDirectory =>
        DuckyPaths.ThemesDirectory;

    public static List<ThemeData> LoadAll()
    {
        Directory.CreateDirectory(ThemesDirectory);

        List<ThemeData> themes = new();

        foreach (string directory in Directory.GetDirectories(ThemesDirectory))
        {
            string path = Path.Combine(directory, "theme.conf");

            if (!File.Exists(path))
                continue;

            try
            {
                themes.Add(Load(path));
            }
            catch (Exception ex)
            {
                DuckyLog.Log("Could not load theme: " + ex.Message);
            }
        }

        return themes;
    }

    public static ThemeData Load(string path)
    {
        ThemeData theme = new();

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("#"))
                continue;

            int separator = line.IndexOf('=');

            if (separator < 0)
                continue;

            string key = line[..separator].Trim().ToLowerInvariant();
            string value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "id":
                    theme.Id = value;
                    break;
                case "name":
                    theme.Name = value;
                    break;
                case "is-built-in-default":
                    if (bool.TryParse(value, out bool isBuiltInDefault))
                        theme.IsBuiltInDefault = isBuiltInDefault;
                    break;
                case "background-color":
                    theme.BackgroundColor = value;
                    break;
                case "terminal-opacity":
                    if (double.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        out double terminalOpacity))
                    {
                        theme.TerminalOpacity = Math.Clamp(terminalOpacity, 0, 100);
                    }
                    break;
                case "text-color":
                    theme.TextColor = value;
                    break;
                case "cursor-color":
                    theme.CursorColor = value;
                    break;
                case "selection-color":
                    theme.SelectionColor = value;
                    break;
                case "selection-text-color":
                    theme.SelectionTextColor = value;
                    break;
                case "navbar-background-color":
                    theme.NavbarBackgroundColor = value;
                    break;
                case "navbar-title-text-color":
                    theme.NavbarTitleTextColor = value;
                    break;
                case "navbar-button-color":
                    theme.NavbarButtonColor = value;
                    break;
                case "navbar-button-text-color":
                    theme.NavbarButtonTextColor = value;
                    break;
                case "workspace-label-background-color":
                    theme.WorkspaceLabelBackgroundColor = value;
                    break;
                case "workspace-label-text-color":
                    theme.WorkspaceLabelTextColor = value;
                    break;
                case "manager-background-color":
                    theme.ManagerBackgroundColor = value;
                    break;
                case "manager-sidebar-color":
                    theme.ManagerSidebarColor = value;
                    break;
                case "manager-card-color":
                    theme.ManagerCardColor = value;
                    break;
                case "manager-text-color":
                    theme.ManagerTextColor = value;
                    break;
                case "manager-accent-color":
                    theme.ManagerAccentColor = value;
                    break;
                case "manager-button-color":
                    theme.ManagerButtonColor = value;
                    break;
                case "manager-button-text-color":
                    theme.ManagerButtonTextColor = value;
                    break;
                case "font-family":
                    theme.FontFamily = value;
                    break;
                case "font-size":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out double fontSize))
                        theme.FontSize = fontSize;
                    break;
                case "font-weight":
                    theme.FontWeight = value;
                    break;
                case "line-height":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out double lineHeight))
                        theme.LineHeight = lineHeight;
                    break;
                case "cursor-style":
                    theme.CursorStyle = value;
                    break;
                case "cursor-blink":
                    if (bool.TryParse(value, out bool blink))
                        theme.CursorBlink = blink;
                    break;
                case "cursor-thickness":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out double thickness))
                        theme.CursorThickness = thickness;
                    break;
            }
        }

        return theme;
    }

    public static void Save(ThemeData theme)
    {
        Directory.CreateDirectory(ThemesDirectory);

        string safeName = MakeSafeName(theme.Name);
        string directory = Path.Combine(ThemesDirectory, safeName);
        string? previousThemeFile = FindThemeFile(theme.Id);

        string path = Path.Combine(directory, "theme.conf");
        if (File.Exists(path) &&
            !string.Equals(path, previousThemeFile, StringComparison.OrdinalIgnoreCase) &&
            Load(path).Id != theme.Id)
        {
            directory = Path.Combine(ThemesDirectory, safeName + "-" + theme.Id);
            path = Path.Combine(directory, "theme.conf");
        }

        Directory.CreateDirectory(directory);

        string contents =
            $"id = {theme.Id}\n" +
            $"name = {theme.Name}\n" +
            $"is-built-in-default = {theme.IsBuiltInDefault.ToString().ToLowerInvariant()}\n" +
            $"background-color = {theme.BackgroundColor}\n" +
            $"terminal-opacity = {theme.TerminalOpacity.ToString(CultureInfo.InvariantCulture)}\n" +
            $"text-color = {theme.TextColor}\n" +
            $"cursor-color = {theme.CursorColor}\n" +
            $"selection-color = {theme.SelectionColor}\n" +
            $"selection-text-color = {theme.SelectionTextColor}\n" +
            $"navbar-background-color = {theme.NavbarBackgroundColor}\n" +
            $"navbar-title-text-color = {theme.NavbarTitleTextColor}\n" +
            $"navbar-button-color = {theme.NavbarButtonColor}\n" +
            $"navbar-button-text-color = {theme.NavbarButtonTextColor}\n" +
            $"workspace-label-background-color = {theme.WorkspaceLabelBackgroundColor}\n" +
            $"workspace-label-text-color = {theme.WorkspaceLabelTextColor}\n" +
            $"manager-background-color = {theme.ManagerBackgroundColor}\n" +
            $"manager-sidebar-color = {theme.ManagerSidebarColor}\n" +
            $"manager-card-color = {theme.ManagerCardColor}\n" +
            $"manager-text-color = {theme.ManagerTextColor}\n" +
            $"manager-accent-color = {theme.ManagerAccentColor}\n" +
            $"manager-button-color = {theme.ManagerButtonColor}\n" +
            $"manager-button-text-color = {theme.ManagerButtonTextColor}\n" +
            $"font-family = {theme.FontFamily}\n" +
            $"font-size = {theme.FontSize.ToString(CultureInfo.InvariantCulture)}\n" +
            $"font-weight = {theme.FontWeight}\n" +
            $"line-height = {theme.LineHeight.ToString(CultureInfo.InvariantCulture)}\n" +
            $"cursor-style = {theme.CursorStyle}\n" +
            $"cursor-blink = {theme.CursorBlink.ToString().ToLowerInvariant()}\n" +
            $"cursor-thickness = {theme.CursorThickness.ToString(CultureInfo.InvariantCulture)}\n";

        File.WriteAllText(path, contents);

        if (previousThemeFile is not null &&
            !string.Equals(previousThemeFile, path, StringComparison.OrdinalIgnoreCase))
        {
            string previousDirectory = Path.GetDirectoryName(previousThemeFile)!;
            File.Delete(previousThemeFile);

            if (Directory.Exists(previousDirectory) &&
                !Directory.EnumerateFileSystemEntries(previousDirectory).Any())
            {
                Directory.Delete(previousDirectory);
            }
        }
    }

    public static void Delete(ThemeData theme)
    {
        string? themeFile = FindThemeFile(theme.Id);

        if (themeFile is null)
            return;

        string directory = Path.GetDirectoryName(themeFile)!;
        File.Delete(themeFile);

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }

    public static string MakeSafeName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        name = name.Trim();

        return string.IsNullOrWhiteSpace(name)
            ? "New Theme"
            : name;
    }

    private static string? FindThemeFile(string themeId)
    {
        foreach (string directory in Directory.GetDirectories(ThemesDirectory))
        {
            string path = Path.Combine(directory, "theme.conf");
            if (!File.Exists(path))
                continue;

            try
            {
                if (Load(path).Id == themeId)
                    return path;
            }
            catch (Exception ex)
            {
                DuckyLog.Log("Could not inspect theme while saving: " + ex.Message);
            }
        }

        return null;
    }
}
