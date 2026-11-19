using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ducky.Updater.Core;

public sealed class GitHubReleaseClient
{
    private const long MaximumDownloadSize = 1_500_000_000;
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private IReadOnlyList<GitHubRelease>? cachedReleases;
    private DateTimeOffset cacheExpiresAt;

    public async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh &&
                cachedReleases is not null &&
                DateTimeOffset.UtcNow < cacheExpiresAt)
            {
                return cachedReleases;
            }

            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));

            using HttpRequestMessage request = new(
                HttpMethod.Get,
                UpdateRepository.ApiUrl);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new HttpRequestException(
                    "GitHub returned 404 for the Ducky releases API. " +
                    "The deticek/Ducky repository must be public and have at least one published release.");
            }

            response.EnsureSuccessStatusCode();
            await using Stream content = await response.Content.ReadAsStreamAsync(
                timeout.Token);
            using JsonDocument document = await JsonDocument.ParseAsync(
                content,
                cancellationToken: timeout.Token);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(
                    "GitHub returned an invalid release list.");

            List<GitHubRelease> releases = new();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                GitHubRelease? release = TryParseRelease(element);
                if (release is not null)
                    releases.Add(release);
            }

            cachedReleases = releases
                .OrderByDescending(release => release.Version)
                .ThenByDescending(release => release.PublishedAt)
                .ToArray();
            cacheExpiresAt = DateTimeOffset.UtcNow + CacheDuration;
            return cachedReleases;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task<GitHubRelease?> GetLatestStableReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubRelease> releases =
            await GetReleasesAsync(cancellationToken: cancellationToken);
        return releases.FirstOrDefault(release =>
            !release.IsPrerelease &&
            release.PackageUri is not null &&
            release.ChecksumUri is not null);
    }

    public async Task<GitHubRelease?> FindReleaseAsync(
        SemanticVersion version,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubRelease> releases =
            await GetReleasesAsync(cancellationToken: cancellationToken);
        return releases.FirstOrDefault(release => release.Version == version);
    }

    public async Task DownloadVerifiedAssetAsync(
        Uri assetUri,
        Uri checksumUri,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string checksumText = await HttpClient.GetStringAsync(
            checksumUri,
            cancellationToken);
        string expectedHash = checksumText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        if (expectedHash.Length != 64 ||
            !expectedHash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "The SHA-256 checksum asset is invalid.");
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullDestination);
        if (directory is null)
            throw new InvalidOperationException(
                "The download destination has no parent directory.");

        Directory.CreateDirectory(directory);
        string partialPath = fullDestination + ".partial";
        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                assetUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes > MaximumDownloadSize)
                throw new InvalidDataException(
                    "The update asset exceeds the maximum permitted size.");

            await using Stream source = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            await using FileStream destination = new(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = new byte[81920];
            long bytesReceived = 0;
            while (true)
            {
                int read = await source.ReadAsync(
                    buffer,
                    cancellationToken);
                if (read == 0)
                    break;

                bytesReceived += read;
                if (bytesReceived > MaximumDownloadSize)
                    throw new InvalidDataException(
                        "The update asset exceeds the maximum permitted size.");

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
                progress?.Report(new DownloadProgress(
                    bytesReceived,
                    totalBytes));
            }

            await destination.FlushAsync(cancellationToken);
            await destination.DisposeAsync();

            await using FileStream package = File.OpenRead(partialPath);
            byte[] hash = await SHA256.HashDataAsync(
                package,
                cancellationToken);
            string actualHash = Convert.ToHexString(hash);
            if (!actualHash.Equals(
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The downloaded asset failed SHA-256 verification.");
            }

            File.Move(partialPath, fullDestination, true);
        }
        catch
        {
            if (File.Exists(partialPath))
                File.Delete(partialPath);
            throw;
        }
    }

    private static GitHubRelease? TryParseRelease(JsonElement element)
    {
        if (GetBoolean(element, "draft"))
            return null;

        string tagName = GetString(element, "tag_name");
        if (!SemanticVersion.TryParse(tagName, out SemanticVersion version))
            return null;

        bool isPrerelease = GetBoolean(element, "prerelease");
        string name = GetString(element, "name");
        string notes = GetString(element, "body");
        DateTimeOffset publishedAt = DateTimeOffset.MinValue;
        if (DateTimeOffset.TryParse(
                GetString(element, "published_at"),
                out DateTimeOffset parsedDate))
        {
            publishedAt = parsedDate;
        }

        Dictionary<string, Uri> assets = new(StringComparer.Ordinal);
        if (element.TryGetProperty("assets", out JsonElement assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assetsElement.EnumerateArray())
            {
                string assetName = GetString(asset, "name");
                string downloadUrl = GetString(asset, "browser_download_url");
                if (Uri.TryCreate(
                        downloadUrl,
                        UriKind.Absolute,
                        out Uri? uri) &&
                    uri.Scheme == Uri.UriSchemeHttps &&
                    uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    assets[assetName] = uri;
                }
            }
        }

        string packageName = UpdateRepository.GetPackageName(version);
        string bootstrapperName = UpdateRepository.GetBootstrapperName();
        return new GitHubRelease(
            tagName,
            name,
            notes,
            publishedAt,
            isPrerelease,
            version,
            assets.GetValueOrDefault(packageName),
            assets.GetValueOrDefault(
                UpdateRepository.GetPackageChecksumName(version)),
            assets.GetValueOrDefault(bootstrapperName),
            assets.GetValueOrDefault(
                UpdateRepository.GetBootstrapperChecksumName()));
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.True;

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Ducky-Updater/1.0");
        return client;
    }
}

public static class UpdateOperationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Write(UpdateOperationStatus status)
    {
        Directory.CreateDirectory(UpdatePaths.DataDirectory);
        string temporaryPath = UpdatePaths.OperationFilePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(status, JsonOptions));
        File.Move(temporaryPath, UpdatePaths.OperationFilePath, true);
    }

    public static UpdateOperationStatus? Read()
    {
        if (!File.Exists(UpdatePaths.OperationFilePath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<UpdateOperationStatus>(
                File.ReadAllText(UpdatePaths.OperationFilePath),
                JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }
}
