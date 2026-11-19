using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Ducky.Updater.Core;

namespace ducky;

public partial class UpdatesView : UserControl
{
    private readonly GitHubReleaseClient releaseClient = new();
    private GitHubRelease? latestStableRelease;
    private bool loading;

    public UpdatesView()
    {
        InitializeComponent();
        loading = true;
        CurrentVersionText.Text = "v" + DuckyUpdateService.CurrentVersion;

        ManagerSettingsData settings = ManagerSettingsStore.Load();
        AutoCheckForUpdatesCheckBox.IsChecked =
            settings.AutomaticallyCheckForUpdates;
        AutoUpdateCheckBox.IsChecked =
            settings.AutomaticallyInstallUpdates;
        loading = false;

        if (!OperatingSystem.IsWindows())
        {
            AutoCheckForUpdatesCheckBox.IsEnabled = false;
            AutoUpdateCheckBox.IsEnabled = false;
            CheckForUpdatesButton.IsEnabled = false;
            RollbackButton.IsEnabled = false;
            UpdateStatusText.Text =
                "In-app updates are currently supported on Windows only. Download the package for your platform from GitHub Releases.";
            return;
        }

        AutoCheckForUpdatesCheckBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
                SaveUpdatePreferences();
        };
        AutoUpdateCheckBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
                SaveUpdatePreferences();
        };
    }

    private void SaveUpdatePreferences()
    {
        if (loading)
            return;

        try
        {
            ManagerSettingsData settings = ManagerSettingsStore.Load();
            settings.AutomaticallyCheckForUpdates =
                AutoCheckForUpdatesCheckBox.IsChecked == true;
            settings.AutomaticallyInstallUpdates =
                AutoUpdateCheckBox.IsChecked == true;
            ManagerSettingsStore.Save(settings);
            UpdatePreferencesStatusText.Text = "Update preferences saved.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not save update preferences: " + ex);
            UpdatePreferencesStatusText.Text =
                "Could not save update preferences: " + ex.Message;
        }
    }

    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsVisible = false;
        UpdateStatusText.Text = "Checking GitHub Releases...";
        LatestVersionText.Text = "Checking...";
        ReleaseNotesText.Text = "";

        try
        {
            IReadOnlyList<GitHubRelease> releases =
                await releaseClient.GetReleasesAsync(forceRefresh: true);
            latestStableRelease =
                await releaseClient.GetLatestStableReleaseAsync();
            VersionHistoryListBox.ItemsSource =
                DuckyUpdateService.BuildVersionHistory(releases);
            VersionHistoryListBox.SelectedItem =
                VersionHistoryListBox.Items
                    .OfType<UpdateVersionOption>()
                    .FirstOrDefault(option => option.IsCurrent);

            if (latestStableRelease is null)
            {
                LatestVersionText.Text = "No stable release";
                UpdateStatusText.Text =
                    "No stable Windows x64 release with a verified package is currently available.";
                return;
            }

            LatestVersionText.Text = "v" + latestStableRelease.Version;
            ReleaseNotesText.Text = latestStableRelease.Notes;
            int comparison = latestStableRelease.Version.CompareTo(
                DuckyUpdateService.CurrentVersion);
            if (comparison > 0)
            {
                UpdateStatusText.Text =
                    $"Update available: v{latestStableRelease.Version}. The package SHA-256 will be verified before installation.";
                InstallUpdateButton.IsVisible = true;
            }
            else if (comparison == 0)
            {
                UpdateStatusText.Text = "Ducky is up to date.";
            }
            else
            {
                UpdateStatusText.Text =
                    $"Installed version v{DuckyUpdateService.CurrentVersion} is newer than the latest stable GitHub release.";
            }
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not check for Ducky updates: " + ex);
            LatestVersionText.Text = "Unavailable";
            UpdateStatusText.Text =
                "Could not check for updates: " + ex.Message;
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private async void InstallUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (latestStableRelease is null)
            return;

        await StartUpdateOperationAsync(
            "update",
            latestStableRelease.Version);
    }

    private async void RollbackUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (VersionHistoryListBox.SelectedItem is not UpdateVersionOption option ||
            !option.CanInstall ||
            option.Version.CompareTo(DuckyUpdateService.CurrentVersion) >= 0)
        {
            return;
        }

        await StartUpdateOperationAsync("rollback", option.Version);
    }

    private void VersionHistory_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) =>
        UpdateRollbackButton();

    private void UpdateRollbackButton()
    {
        RollbackButton.IsEnabled =
            VersionHistoryListBox.SelectedItem is UpdateVersionOption option &&
            option.CanInstall &&
            option.Version.CompareTo(DuckyUpdateService.CurrentVersion) < 0;
    }

    private async Task StartUpdateOperationAsync(
        string operation,
        SemanticVersion version)
    {
        CheckForUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        RollbackButton.IsEnabled = false;
        UpdateProgressBar.IsVisible = true;
        UpdateProgressBar.IsIndeterminate = true;
        UpdateStatusText.Text = operation == "rollback"
            ? $"Preparing rollback to v{version}..."
            : $"Preparing update to v{version}...";

        try
        {
            await DuckyUpdateService.StartOperationAsync(
                operation,
                version,
                ReportUpdateStatus,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            DuckyLog.Log($"Could not {operation} Ducky: " + ex);
            UpdateStatusText.Text =
                $"Could not {operation} Ducky: {ex.Message}";
            UpdateProgressBar.IsVisible = false;
            CheckForUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
            UpdateRollbackButton();
        }
    }

    private void ReportUpdateStatus(UpdateOperationStatus status)
    {
        void UpdateControls()
        {
            UpdateStatusText.Text = status.Message;
            UpdateProgressBar.IsVisible = status.State is
                "preparing" or "downloading" or "verifying";
            if (status.TotalBytes is > 0)
            {
                UpdateProgressBar.IsIndeterminate = false;
                UpdateProgressBar.Value = Math.Clamp(
                    status.BytesReceived * 100d / status.TotalBytes.Value,
                    0,
                    100);
            }
            else
            {
                UpdateProgressBar.IsIndeterminate = true;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            UpdateControls();
        else
            Dispatcher.UIThread.Post(UpdateControls);
    }
}
