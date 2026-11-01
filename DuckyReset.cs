using System;
using System.IO;

namespace ducky;

internal static class DuckyReset
{
    public static void ResetConfig()
    {
        string path = DuckyPaths.ConfigFilePath;

        if (File.Exists(path))
            File.Delete(path);

        // Ponovno ustvari privzeto konfiguracijo.
        _ = DuckyPaths.ConfigFilePath;
    }

    public static void ResetWorkspaces()
    {
        string path = DuckyPaths.WorkspaceStatePath;

        if (File.Exists(path))
            File.Delete(path);
    }

    public static void ResetAll()
    {
        ResetConfig();
        ResetWorkspaces();
    }
}