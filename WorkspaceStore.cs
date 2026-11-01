using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ducky;

internal sealed class WorkspaceSessionState
{
    public int ActiveWorkspaceIndex { get; set; }
    public List<WorkspaceLayoutState> Workspaces { get; set; } = new();
}

internal static class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string StatePath => DuckyPaths.WorkspaceStatePath;

    public static WorkspaceSessionState? Load()
    {
        string path = StatePath;
        if (!File.Exists(path))
            return null;

        string json = File.ReadAllText(path);
        return Deserialize(json);
    }

    public static void Save(WorkspaceSessionState state)
    {
        string path = StatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            Serialize(state));
        File.Move(temporaryPath, path, overwrite: true);
    }

    internal static string Serialize(WorkspaceSessionState state) =>
        JsonSerializer.Serialize(state, JsonOptions);

    internal static WorkspaceSessionState? Deserialize(string json) =>
        JsonSerializer.Deserialize<WorkspaceSessionState>(json, JsonOptions);
}
