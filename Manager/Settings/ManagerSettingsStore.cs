using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace ducky;

internal sealed class ManagerSettingsData
{
    public static readonly string[] DefaultSidebarOrder =
    [
        "Overview", "Workspaces", "Custom Commands", "Configuration",
        "Timer", "Discord integration", "Logs",
        "Updates", "Themes", "Shortcuts"
    ];

    public string Theme { get; set; } = "System";
    public int UiScale { get; set; } = 100;
    public string SidebarBehavior { get; set; } = "Expanded";
    public List<string> SidebarOrder { get; set; } = [.. DefaultSidebarOrder];
    public bool StartOnStartup { get; set; }
    public bool ConfirmBeforeClosing { get; set; } = true;
    public string Language { get; set; } = "English";
    public bool UpdateNotifications { get; set; } = true;
    public bool AutomaticallyCheckForUpdates { get; set; } = true;
    public bool AutomaticallyInstallUpdates { get; set; }
    public bool ErrorNotifications { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public int SoundVolume { get; set; } = 50;
    public bool OpenManagerOnStartup { get; set; }
}

internal static class ManagerSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string FilePath =>
        Path.Combine(DuckyPaths.DataDirectory, "manager-settings.json");

    public static ManagerSettingsData Load()
    {
        if (!File.Exists(FilePath))
            return new ManagerSettingsData();

        try
        {
            ManagerSettingsData settings =
                JsonSerializer.Deserialize<ManagerSettingsData>(
                    File.ReadAllText(FilePath),
                    Options) ?? new ManagerSettingsData();
            NormalizeSidebarOrder(settings);
            return settings;
        }
        catch (System.Exception ex)
        {
            DuckyLog.Log("Could not load Manager settings: " + ex);
            return new ManagerSettingsData();
        }
    }

    public static void Save(ManagerSettingsData settings)
    {
        NormalizeSidebarOrder(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
        File.Move(temporaryPath, FilePath, true);
    }

    private static void NormalizeSidebarOrder(ManagerSettingsData settings)
    {
        List<string> normalized = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string item in settings.SidebarOrder ?? [])
        {
            string? canonical = null;
            foreach (string candidate in ManagerSettingsData.DefaultSidebarOrder)
            {
                if (string.Equals(candidate, item, StringComparison.OrdinalIgnoreCase))
                {
                    canonical = candidate;
                    break;
                }
            }

            if (canonical is not null && seen.Add(canonical))
                normalized.Add(canonical);
        }

        foreach (string item in ManagerSettingsData.DefaultSidebarOrder)
        {
            if (seen.Add(item))
                normalized.Add(item);
        }

        settings.SidebarOrder = normalized;
    }
}
