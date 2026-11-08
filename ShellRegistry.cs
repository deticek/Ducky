using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ducky;

internal static class ShellRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string FilePath =>
        Path.Combine(DuckyPaths.DataDirectory, "shells.json");

    private static string RegistryPath => FilePath;

    public static List<ShellProfile> Load()
    {
        string path = RegistryPath;
        if (File.Exists(path))
        {
            List<ShellProfile>? profiles =
                JsonSerializer.Deserialize<List<ShellProfile>>(
                    File.ReadAllText(path),
                    JsonOptions);
            if (profiles is { Count: > 0 })
                return profiles;
        }

        List<ShellProfile> defaults = CreateDefaults();
        Save(defaults);
        return defaults;
    }

    public static void Save(IEnumerable<ShellProfile> profiles)
    {
        string path = RegistryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(profiles, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public static ShellProfile? Resolve(string? value)
    {
        List<ShellProfile> profiles = Load();
        if (!string.IsNullOrWhiteSpace(value))
        {
            ShellProfile? found = profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Name, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Executable, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetFileNameWithoutExtension(profile.Executable),
                    Path.GetFileNameWithoutExtension(value),
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.ExecutablePath, value, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                return found;
        }

        Config config = ConfigReder.GetConfig();
        return profiles.FirstOrDefault(profile =>
                   string.Equals(profile.Executable, config.Shell, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(profile.ExecutablePath, config.ShellPath, StringComparison.OrdinalIgnoreCase))
               ?? profiles[0];
    }

    private static List<ShellProfile> CreateDefaults()
    {
        string defaultShell = ConfigReder.GetConfig().Shell;
        string[] candidates = OperatingSystem.IsWindows()
            ? ["cmd.exe", "powershell.exe", "pwsh.exe", "bash.exe", "nu.exe"]
            : ["bash", "zsh", "sh", "fish", "nu"];

        List<ShellProfile> profiles = candidates
            .Where(IsAvailable)
            .Select(command => new ShellProfile
            {
                Name = Path.GetFileNameWithoutExtension(command) switch
                {
                    "cmd" => "Command Prompt",
                    "powershell" => "Windows PowerShell",
                    "pwsh" => "PowerShell",
                    "nu" => "Nu Shell",
                    _ => Path.GetFileNameWithoutExtension(command)
                },
                Executable = command
            })
            .ToList();

        if (!profiles.Any(profile =>
                string.Equals(profile.Executable, defaultShell, StringComparison.OrdinalIgnoreCase)))
        {
            profiles.Insert(0, new ShellProfile
            {
                Name = Path.GetFileNameWithoutExtension(defaultShell),
                Executable = defaultShell
            });
        }

        return profiles;
    }

    private static bool IsAvailable(string executable)
    {
        if (Path.IsPathRooted(executable))
            return File.Exists(executable);

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
            return false;

        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (string directory in path.Split(Path.PathSeparator))
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(
                    directory,
                    Path.HasExtension(executable)
                        ? executable
                        : executable + extension);
                if (File.Exists(candidate))
                    return true;
            }
        }

        return false;
    }
}
