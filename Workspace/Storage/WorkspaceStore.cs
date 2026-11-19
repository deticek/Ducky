
using System;
using System.IO;
using System.Text.Json;

namespace ducky;

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

        WorkspaceSessionState? state =
            JsonSerializer.Deserialize<WorkspaceSessionState>(json, JsonOptions);

        if (state is null)
            return null;

        bool migrated = Normalize(state);

        if (migrated)
            Save(state);

        return state;
    }

    public static void Save(WorkspaceSessionState state)
    {
        string path = StatePath;

        string? directory = Path.GetDirectoryName(path);

        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";

        string json = JsonSerializer.Serialize(state, JsonOptions);

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, true);
    }

    private static bool Normalize(WorkspaceSessionState state)
    {
        bool changed = false;

        if (state.Workspaces is null)
        {
            state.Workspaces = new();
            changed = true;
        }

        if (state.TemporaryWorkspace is null)
        {
            state.TemporaryWorkspace = new();
            changed = true;
        }

        for (int i = 0; i < state.Workspaces.Count; i++)
        {
            WorkspaceData workspace = state.Workspaces[i];

            if (string.IsNullOrWhiteSpace(workspace.Id))
            {
                workspace.Id = Guid.NewGuid().ToString();
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(workspace.Title) ||
                workspace.Title == "New workspace")
            {
                workspace.Title = "Workspace " + (i + 1);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(workspace.DefaultShell))
            {
                workspace.DefaultShell =
                    ShellRegistry.Resolve(null)?.Id ?? Config.GetDefaultShell();
                changed = true;
            }
        }

        if (state.MaxWorkspaces < 1)
        {
            state.MaxWorkspaces = 20;
            changed = true;
        }

        if (state.Workspaces.Count > 0)
        {
            int activeIndex = Math.Clamp(
                state.ActiveWorkspaceIndex,
                0,
                state.Workspaces.Count - 1);

            if (activeIndex != state.ActiveWorkspaceIndex)
            {
                state.ActiveWorkspaceIndex = activeIndex;
                changed = true;
            }
        }
        else if (state.ActiveWorkspaceIndex != 0)
        {
            state.ActiveWorkspaceIndex = 0;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(state.TemporaryWorkspace.DefaultShell))
        {
            state.TemporaryWorkspace.DefaultShell =
                ShellRegistry.Resolve(null)?.Id ?? Config.GetDefaultShell();
            changed = true;
        }

        return changed;
    }
}
