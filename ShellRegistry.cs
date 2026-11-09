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
        Path.Combine(DuckyPaths.DataDirectory, "shells", "shells.json");

    private static string RegistryPath => FilePath;

    public static List<ShellProfile> Load()
    {
        string path = RegistryPath;
        if (File.Exists(path))
        {
            try
            {
                List<ShellProfile>? profiles =
                    JsonSerializer.Deserialize<List<ShellProfile>>(
                        File.ReadAllText(path),
                        JsonOptions);
                if (profiles is { Count: > 0 })
                {
                    NormalizeProfiles(profiles);
                    return profiles;
                }
            }
            catch (Exception ex)
            {
                DuckyLog.Log("Could not load shell profiles: " + ex);
            }
        }

        List<ShellProfile> defaults = CreateDefaults();
        Save(defaults);
        return defaults;
    }

    public static void Save(IEnumerable<ShellProfile> profiles)
    {
        List<ShellProfile> normalized = profiles.ToList();
        NormalizeProfiles(normalized);

        string path = RegistryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public static List<ShellProfile> DetectInstalled()
    {
        List<ShellProfile> profiles = Load();
        foreach (ShellProfile candidate in CreateDefaults())
        {
            bool exists = profiles.Any(profile =>
                string.Equals(
                    profile.Executable,
                    candidate.Executable,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    profile.ExecutablePath,
                    candidate.Executable,
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(candidate.Executable) &&
                 string.Equals(
                     Path.GetFileName(profile.Executable),
                     Path.GetFileName(candidate.Executable),
                     StringComparison.OrdinalIgnoreCase)));
            if (!exists)
                profiles.Add(candidate);
        }

        Save(profiles);
        return profiles;
    }

    private static void NormalizeProfiles(List<ShellProfile> profiles)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShellProfile profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString();
                ids.Add(profile.Id);
            }

            profile.Executable ??= "";
            profile.ExecutablePath ??= "";
            profile.Arguments ??= "";

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = string.IsNullOrWhiteSpace(profile.Executable)
                    ? "Shell"
                    : Path.GetFileNameWithoutExtension(profile.Executable);
            }
        }
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

    public static ShellProfile? Find(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        List<ShellProfile> profiles = Load();
        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.Executable, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.ExecutablePath, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                Path.GetFileNameWithoutExtension(profile.Executable),
                Path.GetFileNameWithoutExtension(value),
                StringComparison.OrdinalIgnoreCase));
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
