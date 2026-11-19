using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ducky.Updater.Core;

namespace ducky;

internal sealed class UpdateVersionOption(
    SemanticVersion version,
    string label,
    bool isCurrent,
    bool isLocal,
    bool canInstall)
{
    public SemanticVersion Version { get; } = version;
    public string Label { get; } = label;
    public bool IsCurrent { get; } = isCurrent;
    public bool IsLocal { get; } = isLocal;
    public bool CanInstall { get; } = canInstall;

    public override string ToString() => Label;
}

internal static class DuckyUpdateService
{
    public static SemanticVersion CurrentVersion
    {
        get
        {
            string value = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ??
                typeof(App).Assembly.GetName().Version?.ToString(3) ??
                "0.0.0";
            return SemanticVersion.TryParse(value, out SemanticVersion version)
                ? version
                : new SemanticVersion(0, 0, 0);
        }
    }

    public static IReadOnlyList<UpdateVersionOption> BuildVersionHistory(
        IReadOnlyList<GitHubRelease> releases)
    {
        SemanticVersion current = CurrentVersion;
        HashSet<SemanticVersion> localVersions = new() { current };
        if (Directory.Exists(UpdatePaths.VersionsDirectory))
        {
            foreach (string directory in Directory.EnumerateDirectories(
                         UpdatePaths.VersionsDirectory))
            {
                if (SemanticVersion.TryParse(
                        Path.GetFileName(directory),
                        out SemanticVersion version))
                {
                    localVersions.Add(version);
                }
            }
        }

        Dictionary<SemanticVersion, GitHubRelease> releaseByVersion =
            releases
                .GroupBy(release => release.Version)
                .ToDictionary(group => group.Key, group => group.First());
        HashSet<SemanticVersion> versions = new(localVersions);
        versions.UnionWith(releaseByVersion.Keys);

        return versions
            .OrderByDescending(version => version)
            .Select(version =>
            {
                bool isCurrent = version == current;
                bool isLocal = localVersions.Contains(version);
                bool hasRelease =
                    releaseByVersion.TryGetValue(version, out GitHubRelease? release);
                string state = isCurrent
                    ? "Current"
                    : isLocal
                        ? "Installed locally"
                        : release?.IsPrerelease == true
                            ? "GitHub prerelease"
                            : "Available on GitHub";
                bool canInstall = isLocal ||
                    (hasRelease &&
                     release!.PackageUri is not null &&
                     release.ChecksumUri is not null);
                return new UpdateVersionOption(
                    version,
                    $"v{version}  —  {state}",
                    isCurrent,
                    isLocal,
                    canInstall);
            })
            .ToArray();
    }

    public static async Task StartOperationAsync(
        string operation,
        SemanticVersion version,
        Action<UpdateOperationStatus> reportStatus,
        CancellationToken cancellationToken)
    {
        GitHubReleaseClient releaseClient = new();
        GitHubRelease? latest =
            await releaseClient.GetLatestStableReleaseAsync(cancellationToken);
        if (latest?.BootstrapperUri is null ||
            latest.BootstrapperChecksumUri is null)
        {
            throw new InvalidDataException(
                "The latest stable release does not provide the Ducky Bootstrapper and its SHA-256 checksum.");
        }

        Directory.CreateDirectory(UpdatePaths.InstallRoot);
        Directory.CreateDirectory(UpdatePaths.UpdateDirectory);
        string downloadedBootstrapper = Path.Combine(
            UpdatePaths.UpdateDirectory,
            "Ducky.Bootstrapper.exe.new");
        reportStatus(new UpdateOperationStatus(
            "",
            "preparing",
            "Downloading and verifying the Ducky Bootstrapper."));
        IProgress<DownloadProgress> bootstrapperProgress =
            new Progress<DownloadProgress>(progress =>
                reportStatus(new UpdateOperationStatus(
                    "",
                    "preparing",
                    "Downloading the Ducky Bootstrapper.",
                    progress.BytesReceived,
                    progress.TotalBytes)));
        await releaseClient.DownloadVerifiedAssetAsync(
            latest.BootstrapperUri,
            latest.BootstrapperChecksumUri,
            downloadedBootstrapper,
            bootstrapperProgress,
            cancellationToken);
        File.Move(
            downloadedBootstrapper,
            UpdatePaths.BootstrapperPath,
            overwrite: true);

        string operationId = Guid.NewGuid().ToString("N");
        string mode = operation == "rollback" ? "--rollback" : "--update";
        ProcessStartInfo startInfo = new(UpdatePaths.BootstrapperPath)
        {
            WorkingDirectory = UpdatePaths.CurrentDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add(version.ToString());
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--operation-id");
        startInfo.ArgumentList.Add(operationId);
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Could not start the Ducky Bootstrapper.");

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateOperationStatus? status =
                UpdateOperationStateStore.Read();
            if (status?.OperationId == operationId)
            {
                reportStatus(status);
                if (status.State == "ready-to-install")
                {
                    App.ShutdownForUpdate();
                    return;
                }

                if (status.State == "failed")
                {
                    throw new InvalidOperationException(
                        status.Error ?? status.Message);
                }
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Ducky Bootstrapper exited before installation began (exit code {process.ExitCode}). Check {UpdatePaths.LogFilePath} for details.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException(
            "The update operation did not finish preparing within 20 minutes.");
    }
}
