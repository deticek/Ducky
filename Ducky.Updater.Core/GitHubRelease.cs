namespace Ducky.Updater.Core;

public sealed record GitHubRelease(
    string TagName,
    string Name,
    string Notes,
    DateTimeOffset PublishedAt,
    bool IsPrerelease,
    SemanticVersion Version,
    Uri? PackageUri,
    Uri? ChecksumUri,
    Uri? BootstrapperUri,
    Uri? BootstrapperChecksumUri);

public readonly record struct DownloadProgress(
    long BytesReceived,
    long? TotalBytes)
{
    public double? Percentage =>
        TotalBytes is > 0
            ? Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0, 100)
            : null;
}

public sealed record UpdateOperationStatus(
    string OperationId,
    string State,
    string Message,
    long BytesReceived = 0,
    long? TotalBytes = null,
    string? Error = null);

public static class UpdatePaths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ducky");

    public static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "Ducky");

    public static string CurrentDirectory => Path.Combine(InstallRoot, "Current");
    public static string VersionsDirectory => Path.Combine(InstallRoot, "Versions");
    public static string UpdateDirectory => Path.Combine(InstallRoot, "Update");
    public static string DownloadsDirectory =>
        Path.Combine(UpdateDirectory, "downloads");
    public static string StagingDirectory =>
        Path.Combine(UpdateDirectory, "staging");
    public static string OperationFilePath =>
        Path.Combine(DataDirectory, "update-operation.json");
    public static string LogFilePath =>
        Path.Combine(DataDirectory, "update.log");
    public static string BootstrapperPath =>
        Path.Combine(InstallRoot, "Ducky.Bootstrapper.exe");
}
