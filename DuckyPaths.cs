using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ducky;

internal static class DuckyPaths
{
    private const string ApplicationDirectoryName = "Ducky";
    private static readonly PathOverrides CustomPaths = LoadCustomPaths();

    public static string ConfigDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName,
            "Config")
        : Path.Combine(GetXdgDirectory("XDG_CONFIG_HOME", ".config"), "ducky");

    public static string DataDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName)
        : Path.Combine(
            GetXdgDirectory("XDG_DATA_HOME", ".local/share"),
            ApplicationDirectoryName);

    private static string DefaultDataDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName)
        : Path.Combine(
            GetXdgDirectory("XDG_DATA_HOME", ".local/share"),
            ApplicationDirectoryName);

    public static string ThemesFilePath =>
        Path.Combine(DataDirectory, "themes.json");

    public static string ThemesDirectory =>
        Path.Combine(DataDirectory, "Themes");

    public static string ConfigFilePath
    {
        get
        {
            string path = CustomPaths.ConfigFilePath ??
                          Path.Combine(ConfigDirectory, "ducky.conf");
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            RestrictDirectoryPermissions(directory);

            if (!File.Exists(path))
                CreateUserConfig(path);

            return path;
        }
    }

    public static string WorkspaceStatePath =>
        CustomPaths.WorkspaceStatePath ??
        Path.Combine(DataDirectory, "workspaces.json");

    public static string LogFilePath =>
        CustomPaths.LogFilePath ??
        Path.Combine(DataDirectory, "log.txt");

    public static string StoragePathSettingsFilePath =>
        Path.Combine(DefaultDataDirectory, "storage-paths.json");

    public static void SetConfigFilePath(string path)
    {
        CustomPaths.ConfigFilePath = Path.GetFullPath(path);
        SaveCustomPaths();
    }

    public static void SetWorkspaceStatePath(string path)
    {
        CustomPaths.WorkspaceStatePath = Path.GetFullPath(path);
        SaveCustomPaths();
    }

    public static void SetLogFilePath(string path)
    {
        CustomPaths.LogFilePath = Path.GetFullPath(path);
        SaveCustomPaths();
    }

    public static void ImportStoragePathSettings(Stream stream)
    {
        PathOverrides? imported =
            JsonSerializer.Deserialize<PathOverrides>(stream);
        if (imported is null)
            return;

        CustomPaths.ConfigFilePath = PrepareImportedPath(
            imported.ConfigFilePath,
            CustomPaths.ConfigFilePath);
        CustomPaths.WorkspaceStatePath = PrepareImportedPath(
            imported.WorkspaceStatePath,
            CustomPaths.WorkspaceStatePath);
        CustomPaths.LogFilePath = PrepareImportedPath(
            imported.LogFilePath,
            CustomPaths.LogFilePath);
        SaveCustomPaths();
    }

    private static string? PrepareImportedPath(
        string? path,
        string? fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!Path.IsPathRooted(path))
            return fallback;

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                DuckyLog.Log(
                    $"Imported storage location '{path}' is a directory; keeping the current location.");
                return fallback;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return fullPath;
        }
        catch (Exception ex)
        {
            DuckyLog.Log(
                $"Could not use imported storage location '{path}': {ex.Message}");
            return fallback;
        }
    }

    private static PathOverrides LoadCustomPaths()
    {
        string path = StoragePathSettingsFilePath;
        if (!File.Exists(path))
            return new PathOverrides();

        try
        {
            return JsonSerializer.Deserialize<PathOverrides>(
                       File.ReadAllText(path)) ?? new PathOverrides();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                "Could not load storage locations: " + ex);
            return new PathOverrides();
        }
    }

    private static void SaveCustomPaths()
    {
        Directory.CreateDirectory(DefaultDataDirectory);
        string path = StoragePathSettingsFilePath;
        string temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(CustomPaths, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        File.Move(temporaryPath, path, true);
    }

    private sealed class PathOverrides
    {
        public string? ConfigFilePath { get; set; }
        public string? WorkspaceStatePath { get; set; }
        public string? LogFilePath { get; set; }
    }

    private static string GetXdgDirectory(string variable, string fallback)
    {
        string? configuredPath = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(configuredPath) &&
            Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        string homeDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, fallback);
    }

    private static void CreateUserConfig(string destination)
    {
        string legacyConfig = Path.Combine(
            Environment.CurrentDirectory,
            "Config",
            "ducky.conf");
        string bundledConfig = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "ducky.conf");

        string? sourceConfig = File.Exists(legacyConfig)
            ? legacyConfig
            : File.Exists(bundledConfig) ? bundledConfig : null;
        string contents = sourceConfig is not null
            ? File.ReadAllText(sourceConfig)
            : CreateMinimalConfig();

        if (OperatingSystem.IsLinux())
            contents = ConfigureLinuxShell(contents);

        File.WriteAllText(destination, contents, new UTF8Encoding(false));
        RestrictFilePermissions(destination);
    }

    internal static string ConfigureLinuxShell(string contents)
    {
        string shell = GetLinuxDefaultShell();
        string[] lines = contents.Replace("\r\n", "\n").Split('\n');
        bool shellWritten = false;

        for (int index = 0; index < lines.Length; index++)
        {
            string trimmedLine = lines[index].TrimStart();
            if (trimmedLine.StartsWith("#", StringComparison.Ordinal) ||
                !trimmedLine.StartsWith("shell", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int separator = trimmedLine.IndexOf('=');
            if (separator < 0 ||
                !string.Equals(
                    trimmedLine[..separator].Trim(),
                    "shell",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!shellWritten)
            {
                lines[index] = $"shell = {shell}";
                shellWritten = true;
            }
            else
            {
                lines[index] = "# " + trimmedLine;
            }
        }

        if (!shellWritten)
            return $"shell = {shell}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";

        return string.Join(Environment.NewLine, lines);
    }

    internal static string GetLinuxDefaultShell()
    {
        string? userShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(userShell) &&
            Path.IsPathRooted(userShell) &&
            File.Exists(userShell))
        {
            return userShell;
        }

        if (File.Exists("/bin/bash"))
            return "/bin/bash";

        if (File.Exists("/bin/sh"))
            return "/bin/sh";

        return "sh";
    }

    private static string CreateMinimalConfig()
    {
        string shell = OperatingSystem.IsLinux()
            ? GetLinuxDefaultShell()
            : OperatingSystem.IsWindows() ? "cmd.exe" : "sh";

        return $"background-color = #11111B{Environment.NewLine}" +
               $"text-color = #FFFFFF{Environment.NewLine}" +
               $"font-family = Cascadia Mono{Environment.NewLine}" +
               $"font-size = 15{Environment.NewLine}" +
               $"shell = {shell}{Environment.NewLine}";
    }

    private static void RestrictDirectoryPermissions(string directory)
    {
        if (!OperatingSystem.IsLinux())
            return;

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }

    private static void RestrictFilePermissions(string file)
    {
        if (!OperatingSystem.IsLinux())
            return;

        File.SetUnixFileMode(
            file,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
