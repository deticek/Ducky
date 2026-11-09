using System;

namespace ducky;

public sealed class ShellProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string ChangeDirectoryCommand { get; set; } = "cd \"{0}\"";
}