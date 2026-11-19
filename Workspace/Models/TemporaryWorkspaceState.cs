using System;
using System.IO;

namespace ducky;

internal sealed class TemporaryWorkspaceState
{
    public string WorkingDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string DefaultShell { get; set; } = Config.GetDefaultShell();
    public string KeyShortcut { get; set; } = "";
    public string? ThemeId { get; set; }

    public WorkspacePaneState? Root { get; set; }

    public int ActivePaneIndex { get; set; }
}