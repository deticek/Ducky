using System;
using System.IO;

namespace ducky;

public class DuckyLog
{
    public static void Log(string input)
    {
        string path = DuckyPaths.LogFilePath;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] : {input}{Environment.NewLine}");
    }

    public static void JustLog(string input)
    {
        string path = DuckyPaths.LogFilePath;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.AppendAllText(path, $"{input}{Environment.NewLine}");
    }
}