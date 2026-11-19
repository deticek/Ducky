using Ducky.Updater.Core;

namespace Ducky.Bootstrapper;

internal sealed class UpdateLogger
{
    private readonly object sync = new();

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        string line =
            $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] [{level}] {message}{Environment.NewLine}";
        Directory.CreateDirectory(UpdatePaths.DataDirectory);
        lock (sync)
            File.AppendAllText(UpdatePaths.LogFilePath, line);
    }
}
