using System;

namespace ducky;

internal sealed class WorkspaceData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New workspace";
    public string DefaultShell { get; set; } = "cmd.exe";
    public string KeyShortcut { get; set; } = "";
    public string? ThemeId { get; set; }

    public WorkspacePaneState? Root { get; set; }

    public int ActivePaneIndex { get; set; }
}