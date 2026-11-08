using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace ducky;

internal sealed class ManagerSettingsData
{
    public string Theme { get; set; } = "System";
    public int UiScale { get; set; } = 100;
    public string SidebarBehavior { get; set; } = "Expanded";
    public List<string> SidebarOrder { get; set; } =
    [
        "Overview", "Workspaces", "Custom Commands", "Configuration",
        "Discord Integration", "Themes", "Shortcuts", "Logs"
    ];
    public bool StartOnStartup { get; set; }
    public bool ConfirmBeforeClosing { get; set; } = true;
    public string Language { get; set; } = "English";
    public bool UpdateNotifications { get; set; } = true;
    public bool ErrorNotifications { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public int SoundVolume { get; set; } = 50;
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
            return JsonSerializer.Deserialize<ManagerSettingsData>(
                       File.ReadAllText(FilePath),
                       Options) ?? new ManagerSettingsData();
        }
        catch (System.Exception ex)
        {
            DuckyLog.Log("Could not load Manager settings: " + ex);
            throw;
        }
    }

    public static void Save(ManagerSettingsData settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
        File.Move(temporaryPath, FilePath, true);
    }
}
