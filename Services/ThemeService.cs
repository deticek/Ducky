using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ducky;

internal static class ThemeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static event Action? Changed;

    public static void NotifyDataReloaded() => Changed?.Invoke();

    public static List<ThemeData> LoadAll()
    {
        List<ThemeData> themes = ThemeStore.LoadAll();
        ThemeCollection collection = LoadCollection();

        ThemeData? builtInDefault = string.IsNullOrWhiteSpace(
                collection.BuiltInDefaultThemeId)
            ? null
            : themes.FirstOrDefault(
                theme => theme.Id == collection.BuiltInDefaultThemeId);

        if (builtInDefault is null)
        {
            foreach (ThemeData theme in themes.Where(
                         item => item.IsBuiltInDefault))
            {
                theme.IsBuiltInDefault = false;
                ThemeStore.Save(theme);
            }

            builtInDefault = new ThemeData
            {
                Name = "Default",
                IsBuiltInDefault = true
            };
            ThemeStore.Save(builtInDefault);
            themes.Add(builtInDefault);
            collection.BuiltInDefaultThemeId = builtInDefault.Id;
            collection.DefaultThemeId = builtInDefault.Id;
            SaveCollection(collection);
        }
        else
        {
            foreach (ThemeData theme in themes)
            {
                bool shouldBeBuiltIn = theme.Id == builtInDefault.Id;
                if (theme.IsBuiltInDefault == shouldBeBuiltIn)
                    continue;

                theme.IsBuiltInDefault = shouldBeBuiltIn;
                ThemeStore.Save(theme);
            }

            if (!themes.Any(theme => theme.Id == collection.DefaultThemeId))
            {
                collection.DefaultThemeId = builtInDefault.Id;
                SaveCollection(collection);
            }
        }

        return themes;
    }

    public static ThemeData? GetTheme(string? themeId)
    {
        List<ThemeData> themes = LoadAll();
        ThemeData? defaultTheme = GetDefaultTheme(themes);

        return string.IsNullOrWhiteSpace(themeId)
            ? defaultTheme
            : themes.FirstOrDefault(theme => theme.Id == themeId) ?? defaultTheme;
    }

    public static void SetDefaultTheme(string themeId)
    {
        if (!LoadAll().Any(theme => theme.Id == themeId))
            throw new InvalidOperationException("The selected theme no longer exists.");

        SaveDefaultThemeId(themeId);
        Changed?.Invoke();
    }

    public static void NotifyThemeSaved(ThemeData theme)
    {
        if (!LoadAll().Any(item => item.Id == theme.Id))
            throw new InvalidOperationException("The saved theme could not be found.");

        Changed?.Invoke();
    }

    public static void DeleteTheme(ThemeData theme)
    {
        if (theme.IsBuiltInDefault)
            throw new InvalidOperationException("The built-in default theme cannot be deleted.");

        ThemeStore.Delete(theme);

        List<ThemeData> remainingThemes = ThemeStore.LoadAll();
        string defaultThemeId = LoadCollection().DefaultThemeId;

        if (defaultThemeId == theme.Id ||
            !remainingThemes.Any(item => item.Id == defaultThemeId))
        {
            SaveDefaultThemeId(LoadCollection().BuiltInDefaultThemeId);
        }

        Changed?.Invoke();
    }

    private static ThemeData? GetDefaultTheme(List<ThemeData> themes)
    {
        if (themes.Count == 0)
            return null;

        ThemeCollection collection = LoadCollection();
        ThemeData? selected = themes.FirstOrDefault(
            theme => theme.Id == collection.DefaultThemeId);

        if (selected is not null)
            return selected;

        selected = themes.FirstOrDefault(theme => theme.IsBuiltInDefault) ??
            themes[0];
        SaveDefaultThemeId(selected.Id);
        return selected;
    }

    private static ThemeCollection LoadCollection()
    {
        string path = DuckyPaths.ThemesFilePath;
        if (!File.Exists(path))
            return new ThemeCollection();

        try
        {
            return JsonSerializer.Deserialize<ThemeCollection>(
                       File.ReadAllText(path),
                       JsonOptions)
                   ?? new ThemeCollection();
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not load theme settings: " + ex);
            return new ThemeCollection();
        }
    }

    private static void SaveDefaultThemeId(string themeId)
    {
        ThemeCollection collection = LoadCollection();
        collection.DefaultThemeId = themeId;
        SaveCollection(collection);
    }

    private static void SaveCollection(ThemeCollection collection)
    {
        string path = DuckyPaths.ThemesFilePath;
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(collection, JsonOptions));
        File.Move(temporaryPath, path, true);
    }
}