using System.Diagnostics;
using System.Runtime.InteropServices;
using Ducky.Updater.Core;

namespace Ducky.Bootstrapper;

internal static class Program
{
    private static readonly UpdateLogger Logger = new();

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (EnsureInstalledBootstrapper(args))
                return 0;

            using Semaphore semaphore = new(1, 1, "Local\\DuckyBootstrapper");
            bool lockAcquired = semaphore.WaitOne(0);
            if (!lockAcquired)
                throw new InvalidOperationException(
                    "Ducky updater is already running.");

            try
            {
            Directory.CreateDirectory(UpdatePaths.InstallRoot);
            Directory.CreateDirectory(UpdatePaths.DataDirectory);
            Logger.Info("Ducky Bootstrapper started.");
            GitHubReleaseClient releaseClient = new();
            UpdateInstaller installer = new(releaseClient, Logger);
            Invocation invocation = ParseArguments(args);
            using CancellationTokenSource cancellation = new();

            if (invocation.Mode == "launch")
            {
                await installer.LaunchAsync(
                    invocation.LaunchArguments,
                    cancellation.Token);
                return 0;
            }

            await installer.RunRequestedOperationAsync(
                invocation.Mode,
                invocation.TargetVersion!.Value,
                invocation.WaitForProcessId,
                invocation.OperationId!,
                cancellation.Token);
            return 0;
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            try
            {
                Logger.Error(ex.ToString());
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            ShowError(ex.Message);
            return 1;
        }
    }

    private static bool EnsureInstalledBootstrapper(string[] args)
    {
        string? currentPath = Environment.ProcessPath;
        if (currentPath is null)
            throw new InvalidOperationException(
                "Could not determine the Bootstrapper executable path.");

        if (!string.Equals(
                Path.GetFileName(currentPath),
                "Ducky.Bootstrapper.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string installedPath = Path.GetFullPath(UpdatePaths.BootstrapperPath);
        if (string.Equals(
                Path.GetFullPath(currentPath),
                installedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Directory.CreateDirectory(UpdatePaths.InstallRoot);
        string temporaryPath = installedPath + ".new";
        File.Copy(currentPath, temporaryPath, overwrite: true);
        File.Move(temporaryPath, installedPath, overwrite: true);
        ProcessStartInfo startInfo = new(installedPath)
        {
            WorkingDirectory = Directory.Exists(Environment.CurrentDirectory)
                ? Environment.CurrentDirectory
                : UpdatePaths.InstallRoot,
            UseShellExecute = false
        };
        foreach (string argument in args)
            startInfo.ArgumentList.Add(argument);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException(
                "Could not restart the installed Bootstrapper.");
        return true;
    }

    private static Invocation ParseArguments(string[] args)
    {
        if (args.Length == 0 ||
            string.Equals(args[0], "--launch", StringComparison.OrdinalIgnoreCase))
        {
            return new Invocation(
                "launch",
                null,
                null,
                null,
                args.Skip(1).ToArray());
        }

        string mode = args[0].ToLowerInvariant() switch
        {
            "--update" => "update",
            "--rollback" => "rollback",
            _ => throw new ArgumentException(
                "Usage: Ducky.Bootstrapper.exe --launch | --update <version> | --rollback <version>.")
        };

        if (args.Length < 2 ||
            !SemanticVersion.TryParse(args[1], out SemanticVersion targetVersion))
        {
            throw new ArgumentException(
                "A valid semantic version must be provided.");
        }

        int? waitForProcessId = null;
        string? operationId = null;
        for (int index = 2; index < args.Length; index++)
        {
            if (args[index] == "--wait-pid" &&
                index + 1 < args.Length &&
                int.TryParse(args[++index], out int processId) &&
                processId > 0)
            {
                waitForProcessId = processId;
            }
            else if (args[index] == "--operation-id" &&
                     index + 1 < args.Length &&
                     Guid.TryParseExact(args[++index], "N", out _))
            {
                operationId = args[index];
            }
            else
            {
                throw new ArgumentException(
                    $"Unknown or invalid argument: {args[index]}");
            }
        }

        return new Invocation(
            mode,
            targetVersion,
            waitForProcessId,
            operationId ?? Guid.NewGuid().ToString("N"),
            []);
    }

    private static void ShowError(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            _ = MessageBox(
                IntPtr.Zero,
                message,
                "Ducky Update Error",
                0x10);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr window,
        string text,
        string caption,
        uint type);

    private sealed record Invocation(
        string Mode,
        SemanticVersion? TargetVersion,
        int? WaitForProcessId,
        string? OperationId,
        IReadOnlyList<string> LaunchArguments);
}
