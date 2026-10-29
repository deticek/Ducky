using System;
using System.IO;
using System.Text;

namespace test;

internal static class DuckyPaths
{
    private const string ApplicationDirectoryName = "Ducky";

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

    public static string ConfigFilePath
    {
        get
        {
            string directory = ConfigDirectory;
            Directory.CreateDirectory(directory);
            RestrictDirectoryPermissions(directory);

            string configPath = Path.Combine(directory, "ducky.conf");
            if (!File.Exists(configPath))
                CreateUserConfig(configPath);

            return configPath;
        }
    }

    public static string WorkspaceStatePath =>
        Path.Combine(DataDirectory, "workspaces.json");

    public static string LogFilePath =>
        Path.Combine(DataDirectory, "log.txt");

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
