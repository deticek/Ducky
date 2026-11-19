using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;

namespace ducky;

class Program
{
    public static bool StartInManagerMode { get; private set; }
    public static string? StartupWorkingDirectory { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        List<string> avaloniaArguments = new(args.Length);
        bool suppressWorkingDirectoryInference = false;
        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(
                    args[index],
                    "--startup",
                    StringComparison.OrdinalIgnoreCase))
            {
                suppressWorkingDirectoryInference = true;
                continue;
            }

            if (!string.Equals(
                    args[index],
                    "--working-directory",
                    StringComparison.OrdinalIgnoreCase))
            {
                avaloniaArguments.Add(args[index]);
                continue;
            }

            if (index + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ArgumentException(
                    "The --working-directory option requires a directory path.");
            }

            string directory = Path.GetFullPath(args[++index]);
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException(
                    $"The requested working directory does not exist: {directory}");

            StartupWorkingDirectory = directory;
        }

        if (StartupWorkingDirectory is null &&
            !suppressWorkingDirectoryInference &&
            Directory.Exists(Environment.CurrentDirectory) &&
            !PathsReferToSameDirectory(
                Environment.CurrentDirectory,
                AppContext.BaseDirectory))
        {
            StartupWorkingDirectory =
                Path.GetFullPath(Environment.CurrentDirectory);
        }

        StartInManagerMode = Array.Exists(
            args,
            argument => string.Equals(
                argument,
                "--manager",
                StringComparison.OrdinalIgnoreCase))
                || ManagerSettingsStore.Load().OpenManagerOnStartup;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(
            avaloniaArguments.ToArray());
    }

    private static bool PathsReferToSameDirectory(string first, string second)
    {
        string normalizedFirst = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(first));
        string normalizedSecond = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(second));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedFirst, normalizedSecond, comparison);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
