using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ducky;

public partial class DashboardView : UserControl
{
    public Config config = ConfigReder.GetConfig();

    public DashboardView()
    {
        InitializeComponent();
        SetInformation();
    }

    // LOAD DASHBOARD INFORMATION
    public void SetInformation()
    {
        WorkspaceCountText.Text = config.WorkspaceMax.ToString();
        LogsText.Text = DuckyLog.GetLogs();
    }

    // REFRESH LOGS
    private void RefreshLogs_Click(object? sender, RoutedEventArgs e)
    {
        LogsText.Text = DuckyLog.GetLogs();
    }
}
