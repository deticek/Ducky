using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
using System.IO;

namespace ducky;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        SetInformation();
    }

    private void RefreshLogs_Click(object? sender, RoutedEventArgs e)
    {
        LogsText.Text = DuckyLog.GetLogs();
    }

    private void OpenLogsFolder_Click(object? sender, RoutedEventArgs e)
    {
        string logsPath = Path.GetDirectoryName(DuckyPaths.LogFilePath)!;

        Directory.CreateDirectory(logsPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = logsPath,
            UseShellExecute = true
        });
    }

    private void ClearLogs_Click(object? sender, RoutedEventArgs e)
    {
        string logPath = DuckyPaths.LogFilePath;
    
        if(File.Exists(logPath))
        {
            File.WriteAllText(logPath, string.Empty);
        }
    
        LogsText.Text = "";
    }

    public void SetInformation()
    {
        LogsText.Text = DuckyLog.GetLogs();
    }

}