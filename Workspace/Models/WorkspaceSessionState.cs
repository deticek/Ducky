using System.Collections.Generic;

namespace ducky;

internal sealed class WorkspaceSessionState
{
    public int ActiveWorkspaceIndex { get; set; }

    public int MaxWorkspaces { get; set; } = 20;

    public List<WorkspaceData> Workspaces { get; set; } = new();

    public TemporaryWorkspaceState TemporaryWorkspace { get; set; } = new();
}