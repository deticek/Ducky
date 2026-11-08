namespace ducky;

internal sealed class WorkspacePaneState
{
    public string? WorkingDirectory { get; set; }
    public string? Shell { get; set; }
    public string? Orientation { get; set; }

    public WorkspacePaneState? First { get; set; }
    public WorkspacePaneState? Second { get; set; }
}