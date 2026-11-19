using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Ducky.Updater.Core;

namespace Ducky.Bootstrapper;

internal sealed class UpdateInstaller(
    GitHubReleaseClient releaseClient,
    UpdateLogger logger)
{
    private const int MaximumArchiveEntries = 20000;
    private const long MaximumExpandedSize = 1_500_000_000;
    private const string JournalFileName = "update.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task LaunchAsync(
        IReadOnlyList<string> launchArguments,
        CancellationToken cancellationToken)
    {
        await RecoverInterruptedOperationAsync(cancellationToken);
        string executablePath = Path.Combine(
            UpdatePaths.CurrentDirectory,
            "Ducky.exe");

        if (!File.Exists(executablePath))
        {
            logger.Info("No managed Ducky version exists; installing the latest stable release.");
            GitHubRelease firstRelease =
                await GetLatestStableReleaseAsync(cancellationToken);
            await InstallReleaseAsync(
                firstRelease,
                "initial-install",
                null,
                null,
                cancellationToken);
        }
        else
        {
            UpdateSettings settings = UpdateSettings.Load();
            if (settings.AutomaticallyCheckForUpdates)
            {
                try
                {
                    SemanticVersion? installedVersion =
                        ReadInstalledVersion(UpdatePaths.CurrentDirectory);
                    GitHubRelease? latest =
                        await releaseClient.GetLatestStableReleaseAsync(
                            cancellationToken);
                    if (latest is null)
                    {
                        logger.Info("No stable GitHub release is currently available.");
                    }
                    else if (installedVersion is null ||
                             latest.Version.CompareTo(installedVersion.Value) > 0)
                    {
                        logger.Info(
                            $"Update available: {installedVersion?.ToString() ?? "unknown"} -> {latest.Version}.");
                        if (settings.AutomaticallyInstallUpdates)
                        {
                            await InstallReleaseAsync(
                                latest,
                                "automatic-update",
                                null,
                                null,
                                cancellationToken);
                        }
                    }
                    else
                    {
                        logger.Info(
                            $"Ducky {installedVersion} is up to date (latest stable: {latest.Version}).");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.Error(
                        "Could not check for updates; starting the installed version: " +
                        ex.Message);
                }
            }
        }

        StartDucky(launchArguments);
    }

    public async Task RunRequestedOperationAsync(
        string operation,
        SemanticVersion targetVersion,
        int? waitForProcessId,
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await RecoverInterruptedOperationAsync(cancellationToken);
            SemanticVersion? currentVersion =
                ReadInstalledVersion(UpdatePaths.CurrentDirectory);

            if (operation == "rollback")
            {
                if (currentVersion is null ||
                    targetVersion.CompareTo(currentVersion.Value) >= 0)
                {
                    throw new InvalidOperationException(
                        "Rollback target must be an older version than the installed version.");
                }
            }
            else if (currentVersion is not null &&
                     targetVersion.CompareTo(currentVersion.Value) <= 0)
            {
                throw new InvalidOperationException(
                    "The requested update is not newer than the installed version.");
            }

            await PrepareOperationAsync(
                operation,
                targetVersion,
                operationId,
                cancellationToken);

            UpdateOperationStateStore.Write(new UpdateOperationStatus(
                operationId,
                "ready-to-install",
                "The package is verified. Ducky will close to install it."));

            if (waitForProcessId is > 0)
            {
                await WaitForProcessExitAsync(
                    waitForProcessId.Value,
                    cancellationToken);
            }

            string stagedPath = Path.Combine(
                UpdatePaths.StagingDirectory,
                operationId);
            await ActivateStagedVersionAsync(
                stagedPath,
                targetVersion,
                operationId,
                cancellationToken);
            UpdateOperationStateStore.Write(new UpdateOperationStatus(
                operationId,
                "completed",
                $"Ducky {targetVersion} installed successfully."));
            StartDucky([]);
        }
        catch (Exception ex)
        {
            logger.Error($"Requested {operation} failed: {ex}");
            UpdateOperationStateStore.Write(new UpdateOperationStatus(
                operationId,
                "failed",
                $"Could not {operation} Ducky {targetVersion}.",
                Error: ex.Message));

            try
            {
                await RecoverInterruptedOperationAsync(CancellationToken.None);
            }
            catch (Exception recoveryException)
            {
                logger.Error(
                    "Recovery after the requested operation failed: " +
                    recoveryException);
                throw new AggregateException(
                    "The update failed and recovery also needs attention.",
                    ex,
                    recoveryException);
            }

            if (File.Exists(Path.Combine(UpdatePaths.CurrentDirectory, "Ducky.exe")) &&
                (waitForProcessId is null ||
                 !IsProcessRunning(waitForProcessId.Value)))
            {
                StartDucky([]);
            }

            throw;
        }
    }

    public async Task RecoverInterruptedOperationAsync(
        CancellationToken cancellationToken)
    {
        string journalPath = GetJournalPath();
        if (!File.Exists(journalPath))
            return;

        logger.Info("An interrupted update operation was found; recovering.");
        Journal? journal;
        try
        {
            journal = JsonSerializer.Deserialize<Journal>(
                await File.ReadAllTextAsync(journalPath, cancellationToken),
                JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidDataException(
                "The update recovery journal is unreadable. The installed version was not changed.",
                ex);
        }

        if (journal is null ||
            !Guid.TryParseExact(journal.OperationId, "N", out _) ||
            !SemanticVersion.TryParse(journal.TargetVersion, out SemanticVersion target))
        {
            throw new InvalidDataException(
                "The update recovery journal is invalid. The installed version was not changed.");
        }

        string backupPath = GetBackupPath(journal.OperationId);
        bool currentIsTarget =
            ReadInstalledVersion(UpdatePaths.CurrentDirectory) == target;
        bool retainJournal = false;
        if (currentIsTarget)
        {
            try
            {
                ArchivePreviousVersion(backupPath, journal.PreviousVersion);
            }
            catch (Exception ex)
            {
                retainJournal = true;
                logger.Error(
                    "The active Ducky version is valid, but its backup could not be finalized: " +
                    ex.Message);
            }

            logger.Info($"Recovered completed activation of Ducky {target}.");
        }
        else if (Directory.Exists(backupPath))
        {
            if (Directory.Exists(UpdatePaths.CurrentDirectory))
            {
                string failedPath = Path.Combine(
                    UpdatePaths.UpdateDirectory,
                    "recovery-failed-" + journal.OperationId);
                if (Directory.Exists(failedPath))
                    Directory.Delete(failedPath, recursive: true);
                Directory.Move(UpdatePaths.CurrentDirectory, failedPath);
                Directory.Delete(failedPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(
                UpdatePaths.CurrentDirectory)!);
            Directory.Move(backupPath, UpdatePaths.CurrentDirectory);
            logger.Info("Restored the previous Ducky version after an interrupted update.");
        }
        else if (journal.PreviousVersion is not null &&
                 !Directory.Exists(UpdatePaths.CurrentDirectory))
        {
            throw new InvalidDataException(
                "Recovery could not find either the installed version or its backup.");
        }

        string stagedPath = Path.Combine(
            UpdatePaths.StagingDirectory,
            journal.OperationId);
        try
        {
            if (Directory.Exists(stagedPath))
                Directory.Delete(stagedPath, recursive: true);
            if (!retainJournal)
                File.Delete(journalPath);
            PruneLocalVersions();
        }
        catch (Exception ex)
        {
            logger.Error(
                "Update recovery completed, but cleanup was deferred: " +
                ex.Message);
        }
    }

    private async Task PrepareOperationAsync(
        string operation,
        SemanticVersion targetVersion,
        string operationId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(UpdatePaths.StagingDirectory);
        string stagedPath = Path.Combine(
            UpdatePaths.StagingDirectory,
            operationId);
        if (Directory.Exists(stagedPath))
            Directory.Delete(stagedPath, recursive: true);

        string localVersionPath = Path.Combine(
            UpdatePaths.VersionsDirectory,
            targetVersion.ToString());
        if (operation == "rollback" && Directory.Exists(localVersionPath))
        {
            CopyDirectorySafely(localVersionPath, stagedPath);
            ValidatePackageDirectory(stagedPath, targetVersion);
            return;
        }

        GitHubRelease release =
            await releaseClient.FindReleaseAsync(targetVersion, cancellationToken) ??
            throw new InvalidOperationException(
                $"GitHub release {targetVersion} is not available.");
        if (release.PackageUri is null || release.ChecksumUri is null)
            throw new InvalidDataException(
                $"GitHub release {targetVersion} does not contain a ZIP package and SHA-256 asset.");

        Directory.CreateDirectory(UpdatePaths.DownloadsDirectory);
        string archivePath = Path.Combine(
            UpdatePaths.DownloadsDirectory,
            UpdateRepository.GetPackageName(targetVersion));
        UpdateOperationStateStore.Write(new UpdateOperationStatus(
            operationId,
            "downloading",
            $"Downloading Ducky {targetVersion}."));
        DateTime lastProgressWrite = DateTime.MinValue;
        IProgress<DownloadProgress> progress =
            new InlineProgress<DownloadProgress>(value =>
            {
                if (DateTime.UtcNow - lastProgressWrite <
                        TimeSpan.FromMilliseconds(250) &&
                    value.TotalBytes != value.BytesReceived)
                {
                    return;
                }

                lastProgressWrite = DateTime.UtcNow;
                UpdateOperationStateStore.Write(new UpdateOperationStatus(
                    operationId,
                    "downloading",
                    "Downloading update package.",
                    value.BytesReceived,
                    value.TotalBytes));
            });
        await releaseClient.DownloadVerifiedAssetAsync(
            release.PackageUri,
            release.ChecksumUri,
            archivePath,
            progress,
            cancellationToken);
        logger.Info($"SHA-256 verification successful for Ducky {targetVersion}.");
        UpdateOperationStateStore.Write(new UpdateOperationStatus(
            operationId,
            "verifying",
            "Validating the downloaded package."));
        ExtractAndValidateArchive(archivePath, stagedPath, targetVersion);
    }

    private async Task InstallReleaseAsync(
        GitHubRelease release,
        string operation,
        int? waitForProcessId,
        string? operationId,
        CancellationToken cancellationToken)
    {
        if (release.PackageUri is null || release.ChecksumUri is null)
            throw new InvalidDataException(
                $"GitHub release {release.TagName} has no supported Windows x64 package.");

        operationId ??= Guid.NewGuid().ToString("N");
        await PrepareOperationAsync(
            operation,
            release.Version,
            operationId,
            cancellationToken);
        await ActivateStagedVersionAsync(
            Path.Combine(UpdatePaths.StagingDirectory, operationId),
            release.Version,
            operationId,
            cancellationToken);
        if (waitForProcessId is > 0)
            await WaitForProcessExitAsync(waitForProcessId.Value, cancellationToken);
    }

    private async Task<GitHubRelease> GetLatestStableReleaseAsync(
        CancellationToken cancellationToken)
    {
        return await releaseClient.GetLatestStableReleaseAsync(cancellationToken) ??
               throw new InvalidOperationException(
                   "No stable Ducky release with a Windows x64 package is available.");
    }

    private async Task ActivateStagedVersionAsync(
        string stagedPath,
        SemanticVersion targetVersion,
        string operationId,
        CancellationToken cancellationToken)
    {
        ValidatePackageDirectory(stagedPath, targetVersion);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(UpdatePaths.InstallRoot);
        Directory.CreateDirectory(UpdatePaths.VersionsDirectory);
        Directory.CreateDirectory(UpdatePaths.UpdateDirectory);

        SemanticVersion? previousVersion =
            ReadInstalledVersion(UpdatePaths.CurrentDirectory);
        string backupPath = GetBackupPath(operationId);
        Journal journal = new(
            operationId,
            targetVersion.ToString(),
            previousVersion?.ToString(),
            "prepared");
        WriteJournal(journal);
        logger.Info($"Installing Ducky {targetVersion}.");

        try
        {
            if (Directory.Exists(UpdatePaths.CurrentDirectory))
                Directory.Move(UpdatePaths.CurrentDirectory, backupPath);

            WriteJournal(journal with { Phase = "previous-moved" });
            Directory.Move(stagedPath, UpdatePaths.CurrentDirectory);
            ValidatePackageDirectory(
                UpdatePaths.CurrentDirectory,
                targetVersion);
            WriteJournal(journal with { Phase = "activated" });
        }
        catch (Exception installException)
        {
            logger.Error(
                $"Installing Ducky {targetVersion} failed: {installException.Message}");
            try
            {
                string failedPath = Path.Combine(
                    UpdatePaths.UpdateDirectory,
                    "failed-" + operationId);
                if (Directory.Exists(UpdatePaths.CurrentDirectory))
                {
                    if (Directory.Exists(failedPath))
                        Directory.Delete(failedPath, recursive: true);
                    Directory.Move(
                        UpdatePaths.CurrentDirectory,
                        failedPath);
                }

                if (Directory.Exists(backupPath))
                    Directory.Move(backupPath, UpdatePaths.CurrentDirectory);
                if (Directory.Exists(failedPath))
                    Directory.Delete(failedPath, recursive: true);
                File.Delete(GetJournalPath());
            }
            catch (Exception recoveryException)
            {
                logger.Error(
                    "Immediate update recovery failed; the recovery journal was retained: " +
                    recoveryException.Message);
                throw new AggregateException(
                    "Installation and immediate recovery both failed.",
                    installException,
                    recoveryException);
            }

            throw;
        }

        try
        {
            ArchivePreviousVersion(backupPath, previousVersion?.ToString());
            File.Delete(GetJournalPath());
            PruneLocalVersions();
        }
        catch (Exception finalizationException)
        {
            logger.Error(
                "The new Ducky version is active, but update cleanup was deferred: " +
                finalizationException.Message);
        }

        logger.Info($"Ducky {targetVersion} installed successfully.");
    }

    private void ExtractAndValidateArchive(
        string archivePath,
        string destination,
        SemanticVersion expectedVersion)
    {
        Directory.CreateDirectory(destination);
        using FileStream package = File.OpenRead(archivePath);
        using ZipArchive archive = new(
            package,
            ZipArchiveMode.Read,
            leaveOpen: false);
        if (archive.Entries.Count == 0 ||
            archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException(
                "The update ZIP contains an invalid number of entries.");
        }

        long expandedSize = 0;
        string destinationRoot = Path.GetFullPath(destination) +
                                 Path.DirectorySeparatorChar;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string entryName = entry.FullName.Replace('\\', '/');
            if (entryName.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(entryName) ||
                entryName.Split('/').Any(part =>
                    part is "." or ".." || part.Contains(':')))
            {
                throw new InvalidDataException(
                    "The update ZIP contains an unsafe path.");
            }

            int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
                throw new InvalidDataException(
                    "The update ZIP contains an unsupported symbolic link.");

            expandedSize += entry.Length;
            if (expandedSize > MaximumExpandedSize)
                throw new InvalidDataException(
                    "The expanded update ZIP exceeds the permitted size.");

            string outputPath = Path.GetFullPath(
                Path.Combine(destination, entryName.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
            if (!outputPath.StartsWith(
                    destinationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The update ZIP attempts to write outside its staging folder.");
            }

            if (entryName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using Stream source = entry.Open();
            using FileStream output = new(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            source.CopyTo(output);
        }

        ValidatePackageDirectory(destination, expectedVersion);
    }

    private static void ValidatePackageDirectory(
        string directory,
        SemanticVersion expectedVersion)
    {
        string executablePath = Path.Combine(directory, "Ducky.exe");
        string assemblyPath = Path.Combine(directory, "Ducky.dll");
        string dependenciesPath = Path.Combine(directory, "Ducky.deps.json");
        string runtimePath = Path.Combine(
            directory,
            "Ducky.runtimeconfig.json");
        string manifestPath = Path.Combine(directory, "release-manifest.json");
        if (!File.Exists(executablePath) ||
            !File.Exists(assemblyPath) ||
            !File.Exists(dependenciesPath) ||
            !File.Exists(runtimePath) ||
            !File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                "The update package is missing required Ducky files.");
        }

        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(manifestPath));
        string versionText =
            manifest.RootElement.GetProperty("version").GetString() ?? "";
        string runtime =
            manifest.RootElement.GetProperty("runtime").GetString() ?? "";
        if (!SemanticVersion.TryParse(versionText, out SemanticVersion version) ||
            version != expectedVersion ||
            runtime != "win-x64")
        {
            throw new InvalidDataException(
                "The update package manifest does not match the requested version and runtime.");
        }
    }

    private static void CopyDirectorySafely(string source, string destination)
    {
        string sourceRoot = Path.GetFullPath(source);
        foreach (string directory in Directory.EnumerateDirectories(
                     sourceRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "The local version contains an unsupported link.");
            string relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(
                     sourceRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "The local version contains an unsupported link.");
            string relative = Path.GetRelativePath(sourceRoot, file);
            string outputPath = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.Copy(file, outputPath, overwrite: false);
        }
    }

    private static SemanticVersion? ReadInstalledVersion(string directory)
    {
        string manifestPath = Path.Combine(directory, "release-manifest.json");
        if (!File.Exists(manifestPath))
            return null;

        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(manifestPath));
        string value =
            manifest.RootElement.GetProperty("version").GetString() ?? "";
        return SemanticVersion.TryParse(value, out SemanticVersion version)
            ? version
            : null;
    }

    private void ArchivePreviousVersion(string backupPath, string? versionText)
    {
        if (!Directory.Exists(backupPath))
            return;

        if (!SemanticVersion.TryParse(versionText, out SemanticVersion version))
        {
            Directory.Delete(backupPath, recursive: true);
            return;
        }

        Directory.CreateDirectory(UpdatePaths.VersionsDirectory);
        string versionPath = Path.Combine(
            UpdatePaths.VersionsDirectory,
            version.ToString());
        if (Directory.Exists(versionPath))
        {
            Directory.Delete(backupPath, recursive: true);
            return;
        }

        Directory.Move(backupPath, versionPath);
    }

    private void PruneLocalVersions()
    {
        if (!Directory.Exists(UpdatePaths.VersionsDirectory))
            return;

        List<(string Path, SemanticVersion Version)> versions = new();
        foreach (string directory in Directory.EnumerateDirectories(
                     UpdatePaths.VersionsDirectory))
        {
            string name = Path.GetFileName(directory);
            if (SemanticVersion.TryParse(name, out SemanticVersion version))
                versions.Add((directory, version));
        }

        foreach ((string path, _) in versions
                     .OrderByDescending(item => item.Version)
                     .Skip(2))
        {
            Directory.Delete(path, recursive: true);
            logger.Info($"Removed old local version snapshot {Path.GetFileName(path)}.");
        }
    }

    private static async Task WaitForProcessExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Ducky did not close within ten minutes; the update was not installed.");
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void StartDucky(IReadOnlyList<string> arguments)
    {
        string executablePath = Path.Combine(
            UpdatePaths.CurrentDirectory,
            "Ducky.exe");
        if (!File.Exists(executablePath))
            throw new FileNotFoundException(
                "The installed Ducky executable was not found.",
                executablePath);

        ProcessStartInfo startInfo = new(executablePath)
        {
            WorkingDirectory = Directory.Exists(Environment.CurrentDirectory)
                ? Environment.CurrentDirectory
                : UpdatePaths.CurrentDirectory,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Could not start Ducky.");
    }

    private static string GetJournalPath() =>
        Path.Combine(UpdatePaths.UpdateDirectory, JournalFileName);

    private static string GetBackupPath(string operationId) =>
        Path.Combine(UpdatePaths.UpdateDirectory, "backup-" + operationId);

    private static void WriteJournal(Journal journal)
    {
        Directory.CreateDirectory(UpdatePaths.UpdateDirectory);
        string path = GetJournalPath();
        string temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(journal, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private sealed record Journal(
        string OperationId,
        string TargetVersion,
        string? PreviousVersion,
        string Phase);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
