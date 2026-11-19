namespace Ducky.Updater.Core;

public static class UpdateRepository
{
    public const string Owner = "deticek";
    public const string Name = "Ducky";
    public const string ApiUrl =
        "https://api.github.com/repos/deticek/Ducky/releases?per_page=100";
    public const string RepositoryUrl = "https://github.com/deticek/Ducky";

    public static string GetPackageName(SemanticVersion version) =>
        $"Ducky-{version}-win-x64.zip";

    public static string GetPackageChecksumName(SemanticVersion version) =>
        GetPackageName(version) + ".sha256";

    public static string GetBootstrapperName() =>
        "Ducky.Bootstrapper-win-x64.exe";

    public static string GetBootstrapperChecksumName() =>
        GetBootstrapperName() + ".sha256";
}
