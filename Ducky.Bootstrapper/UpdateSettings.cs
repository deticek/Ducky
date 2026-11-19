using System.Text.Json;
using Ducky.Updater.Core;

namespace Ducky.Bootstrapper;

internal sealed class UpdateSettings
{
    public bool AutomaticallyCheckForUpdates { get; set; } = true;
    public bool AutomaticallyInstallUpdates { get; set; }

    public static UpdateSettings Load()
    {
        string path = Path.Combine(
            UpdatePaths.DataDirectory,
            "manager-settings.json");
        if (!File.Exists(path))
            return new UpdateSettings();

        try
        {
            return JsonSerializer.Deserialize<UpdateSettings>(
                       File.ReadAllText(path)) ??
                   new UpdateSettings();
        }
        catch (JsonException)
        {
            return new UpdateSettings();
        }
    }
}
