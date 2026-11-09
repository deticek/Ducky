using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Win32;

namespace ducky;

public partial class Settings : UserControl
{
    private readonly List<ShellProfile> shellProfiles;
    private bool loading;

    public Settings()
    {
        InitializeComponent();
        loading = true;
        SoundVolumeSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                SoundVolumeText.Text =
                    ((int)SoundVolumeSlider.Value).ToString() + "%";
        };
        LoadManagerSettings(ManagerSettingsStore.Load());
        shellProfiles = ShellRegistry.Load();
        DefaultShellComboBox.ItemsSource = shellProfiles;
        DefaultShellComboBox.DisplayMemberBinding =
            new Binding(nameof(ShellProfile.Name));
        string? defaultShellId =
            ShellRegistry.Resolve(ConfigReder.GetConfig().Shell)?.Id;
        DefaultShellComboBox.SelectedItem = shellProfiles.FirstOrDefault(
            profile => profile.Id == defaultShellId);
        ShowNavbarCheckBox.IsChecked = ConfigReder.GetConfig().ShowNavbar;
        LoadTimerSettings(ConfigReder.GetConfig());
        ConfigLocationTextBox.Text = DuckyPaths.ConfigFilePath;
        WorkspaceLocationTextBox.Text = DuckyPaths.WorkspaceStatePath;
        LogsLocationTextBox.Text = DuckyPaths.LogFilePath;
        RefreshShellList();
        loading = false;

        ThemeComboBox.SelectionChanged += (_, _) => AutoSaveManagerSettings();
        UIScaleComboBox.SelectionChanged += (_, _) => AutoSaveManagerSettings();
        SidebarBehaviorComboBox.SelectionChanged += (_, _) => AutoSaveManagerSettings();

        StartOnStartupCheckBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
                AutoSaveManagerSettings();
        };

        OpenManagerOnStartupCheckBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
                AutoSaveManagerSettings();
        };

        ConfirmBeforeClosingCheckBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
                AutoSaveManagerSettings();
        };

        UpdateNotificationsCheckBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
                AutoSaveManagerSettings();
        };

        SoundEnabledCheckBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty)
                AutoSaveManagerSettings();
        };

        SoundVolumeSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                AutoSaveManagerSettings();
        };
    }

    private void LoadManagerSettings(ManagerSettingsData settings)
    {
        
        List<ThemeData> themes = ThemeService.LoadAll();

        ThemeComboBox.ItemsSource = themes;
        ThemeComboBox.DisplayMemberBinding =
            new Binding(nameof(ThemeData.Name));

        ThemeComboBox.SelectedItem = themes.FirstOrDefault(
            theme => theme.Id == settings.Theme);

        if (ThemeComboBox.SelectedItem is null)
            ThemeComboBox.SelectedItem = ThemeService.GetTheme(null);

        SetComboValue(UIScaleComboBox, settings.UiScale + "%");
        SetComboValue(SidebarBehaviorComboBox, settings.SidebarBehavior);
        StartOnStartupCheckBox.IsChecked = settings.StartOnStartup;
        OpenManagerOnStartupCheckBox.IsChecked = settings.OpenManagerOnStartup;
        ConfirmBeforeClosingCheckBox.IsChecked = settings.ConfirmBeforeClosing;
        UpdateNotificationsCheckBox.IsChecked = settings.UpdateNotifications;
        SoundEnabledCheckBox.IsChecked = settings.SoundEnabled;
        SoundVolumeSlider.Value = settings.SoundVolume;
        SoundVolumeText.Text = settings.SoundVolume + "%";
        SidebarOrderList.Items.Clear();
        foreach (string item in settings.SidebarOrder)
            SidebarOrderList.Items.Add(new ListBoxItem { Content = item });
        if (SidebarOrderList.ItemCount > 0)
            SidebarOrderList.SelectedIndex = 0;
    }

    private static void SetComboValue(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(
                    item.Content?.ToString(),
                    value,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshShellList()
    {
        ShellListPanel.Children.Clear();
        foreach (ShellProfile profile in shellProfiles)
        {
            var card = new Border
            {
                Background = Avalonia.Media.Brushes.Transparent,
                BorderBrush = Avalonia.Media.Brushes.DimGray,
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(8),
                Padding = new Avalonia.Thickness(14)
            };
            var fields = new StackPanel { Spacing = 8 };
            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };
            header.Children.Add(new TextBlock
            {
                Text = profile.Name,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            var remove = new Button
            {
                Content = "Remove",
                Classes = { "settings-button", "danger-button" },
                Tag = profile
            };
            Grid.SetColumn(remove, 1);
            remove.Click += RemoveShell_Click;
            header.Children.Add(remove);
            fields.Children.Add(header);
            fields.Children.Add(CreateShellField(
                "Name",
                profile.Name,
                value =>
                {
                    profile.Name = string.IsNullOrWhiteSpace(value)
                        ? "Shell"
                        : value.Trim();
                    SaveShells();
                }));
            fields.Children.Add(CreateShellField(
                "Executable path or command",
                profile.ExecutablePath.Length > 0
                    ? profile.ExecutablePath
                    : profile.Executable,
                value =>
                {
                    profile.ExecutablePath = Path.IsPathRooted(value)
                        ? value
                        : "";
                    profile.Executable = Path.IsPathRooted(value)
                        ? Path.GetFileName(value)
                        : value.Trim();
                    SaveShells();
                },
                profile));
            fields.Children.Add(CreateShellField(
                "Arguments",
                profile.Arguments,
                value =>
                {
                    profile.Arguments = value;
                    SaveShells();
                }));
            card.Child = fields;
            ShellListPanel.Children.Add(card);
        }
    }

    private Control CreateShellField(
        string label,
        string value,
        Action<string> update,
        ShellProfile? profile = null)
    {
        var field = new StackPanel { Spacing = 5 };
        field.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Avalonia.Media.Brushes.LightGray
        });
        var row = new Grid
        {
            ColumnDefinitions = profile is null
                ? new ColumnDefinitions("*")
                : new ColumnDefinitions("*,Auto")
        };
        var textBox = new TextBox { Text = value };
        if (profile is not null && label == "Executable path or command")
            textBox.Tag = profile;
        textBox.LostFocus += (_, _) =>
        {
            update(textBox.Text ?? "");
            if (profile is not null)
                profile.Name = string.IsNullOrWhiteSpace(profile.Name)
                    ? "Shell"
                    : profile.Name;
            RefreshDefaultShellItems();
        };
        row.Children.Add(textBox);
        if (profile is not null && label == "Executable path or command")
        {
            var browse = new Button
            {
                Content = "Browse",
                Margin = new Avalonia.Thickness(8, 0, 0, 0),
                Classes = { "settings-button" },
                Tag = textBox
            };
            Grid.SetColumn(browse, 1);
            browse.Click += BrowseShell_Click;
            row.Children.Add(browse);
        }
        field.Children.Add(row);
        return field;
    }

    private void RefreshDefaultShellItems()
    {
        string? selectedId =
            (DefaultShellComboBox.SelectedItem as ShellProfile)?.Id;
        DefaultShellComboBox.ItemsSource = null;
        DefaultShellComboBox.ItemsSource = shellProfiles;
        DefaultShellComboBox.DisplayMemberBinding =
            new Binding(nameof(ShellProfile.Name));
        DefaultShellComboBox.SelectedItem =
            shellProfiles.FirstOrDefault(profile => profile.Id == selectedId) ??
            shellProfiles.FirstOrDefault();
    }

    private void SaveShells()
    {
        ShellRegistry.Save(shellProfiles);
        RefreshDefaultShellItems();
    }

    private async void BrowseShell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextBox textBox })
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        IReadOnlyList<IStorageFile> files =
            await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Choose shell executable",
                    AllowMultiple = false
                });
        if (files.Count > 0)
        {
            textBox.Text = files[0].Path.LocalPath;
            if (textBox.Tag is ShellProfile profile)
            {
                profile.ExecutablePath = textBox.Text;
                profile.Executable = Path.GetFileName(textBox.Text);
                SaveShells();
                RefreshShellList();
            }
        }
    }

    private void RemoveShell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShellProfile profile } ||
            shellProfiles.Count <= 1)
            return;

        shellProfiles.Remove(profile);
        if (ReferenceEquals(DefaultShellComboBox.SelectedItem, profile))
            DefaultShellComboBox.SelectedItem = shellProfiles[0];
        SaveShells();
        RefreshShellList();
    }

    private void AddShell_Click(object? sender, RoutedEventArgs e)
    {
        var profile = new ShellProfile
        {
            Name = "New shell",
            Executable = ""
        };
        shellProfiles.Add(profile);
        SaveShells();
        RefreshShellList();
    }

    private void DetectShells_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            List<ShellProfile> detected = ShellRegistry.DetectInstalled();
            shellProfiles.Clear();
            shellProfiles.AddRange(detected);
            RefreshDefaultShellItems();
            RefreshShellList();
            SetStatus("Installed shells detected.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not detect installed shells: " + ex.Message);
        }
    }

    private void SetDefaultShell_Click(object? sender, RoutedEventArgs e)
    {
        if (DefaultShellComboBox.SelectedItem is not ShellProfile profile)
            return;
        if (string.IsNullOrWhiteSpace(profile.Executable))
        {
            SetStatus("Choose an executable for this shell first.");
            return;
        }

        try
        {
            UpdateConfigValues(new Dictionary<string, string>
            {
                ["shell"] = profile.Executable,
                ["shell-path"] = profile.ExecutablePath,
                ["shell-args"] = profile.Arguments
            });
            MainWindow.ReloadConfigFromDisk();
            ReloadConfigControls();
            SetStatus("Default shell updated.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not set the default shell: " + ex.Message);
        }
    }

    private void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (loading)
            return;

        try
        {
            if (DefaultShellComboBox.SelectedItem is ShellProfile profile)
            {
                if (string.IsNullOrWhiteSpace(profile.Executable))
                    throw new InvalidOperationException(
                        "Choose an executable for the selected default shell.");

                UpdateConfigValues(new Dictionary<string, string>
                {
                    ["shell"] = profile.Executable,
                    ["shell-path"] = profile.ExecutablePath,
                    ["shell-args"] = profile.Arguments
                });
            }

            UpdateConfigValues(new Dictionary<string, string>
            {
                ["show-navbar"] = ShowNavbarCheckBox.IsChecked == true
                    ? "true"
                    : "false"
            });
            MainWindow.ReloadConfigFromDisk();
            ManagerSettingsData managerSettings = ReadManagerSettings();
            ManagerSettingsStore.Save(managerSettings);
            ApplyStartupSetting(managerSettings.StartOnStartup);
            TimerSound.Configure(
                managerSettings.SoundEnabled,
                managerSettings.SoundVolume);
            DuckyManager.ApplySettings(managerSettings);
            SetStatus("Settings saved.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save settings: " + ex.Message);
        }
    }

    private void ReloadSettingsFromDisk()
    {
        bool previousLoading = loading;
        loading = true;
        try
        {
            LoadManagerSettings(ManagerSettingsStore.Load());

            shellProfiles.Clear();
            shellProfiles.AddRange(ShellRegistry.Load());
            RefreshDefaultShellItems();
            RefreshShellList();
            ReloadConfigControls();

            ConfigLocationTextBox.Text = DuckyPaths.ConfigFilePath;
            WorkspaceLocationTextBox.Text = DuckyPaths.WorkspaceStatePath;
            LogsLocationTextBox.Text = DuckyPaths.LogFilePath;
        }
        finally
        {
            loading = previousLoading;
        }
    }

    private void ReloadConfigControls()
    {
        Config config = ConfigReder.GetConfig();
        ShowNavbarCheckBox.IsChecked = config.ShowNavbar;
        LoadTimerSettings(config);

        string? defaultShellId = ShellRegistry.Resolve(config.Shell)?.Id;
        DefaultShellComboBox.SelectedItem = shellProfiles.FirstOrDefault(
            profile => profile.Id == defaultShellId) ??
            shellProfiles.FirstOrDefault();
    }

    private static void UpdateConfigValues(
        IReadOnlyDictionary<string, string> values)
    {
        string path = DuckyPaths.ConfigFilePath;
        List<string> lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();
        HashSet<string> updated = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < lines.Count; index++)
        {
            int separator = lines[index].IndexOf('=');
            if (separator < 0)
                continue;
            string key = lines[index][..separator].Trim();
            if (!values.TryGetValue(key, out string? value))
                continue;
            lines[index] = key + " = " + Quote(value);
            updated.Add(key);
        }

        foreach ((string key, string value) in values)
        {
            if (!updated.Contains(key))
                lines.Add(key + " = " + Quote(value));
        }

        File.WriteAllLines(path, lines);
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;

    private void ShowNavbarCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (loading)
            return;

        try
        {
            UpdateConfigValues(new Dictionary<string, string>
            {
                ["show-navbar"] = ShowNavbarCheckBox.IsChecked == true
                    ? "true"
                    : "false"
            });
            MainWindow.ReloadConfigFromDisk();
            SetStatus("Navbar visibility saved.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save navbar visibility: " + ex.Message);
        }
    }

    private void LoadTimerSettings(Config config)
    {
        int hours = (int)config.DefaultCountdownTime.TotalHours;
        TimerHoursTextBox.Text = hours.ToString(CultureInfo.InvariantCulture);
        TimerMinutesTextBox.Text = config.DefaultCountdownTime.Minutes
            .ToString(CultureInfo.InvariantCulture);
        TimerSecondsTextBox.Text = config.DefaultCountdownTime.Seconds
            .ToString(CultureInfo.InvariantCulture);
        TimerStartCommandCheckBox.IsChecked = config.TimerStartCommandEnabled;
        TimerCountdownCommandCheckBox.IsChecked =
            config.TimerCountdownCommandEnabled;
        TimerPauseCommandCheckBox.IsChecked = config.TimerPauseCommandEnabled;
        TimerResetCommandCheckBox.IsChecked = config.TimerResetCommandEnabled;
        TimerCloseCommandCheckBox.IsChecked = config.TimerCloseCommandEnabled;
    }

    private void SaveTimerSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (loading)
            return;

        if (!int.TryParse(
                TimerHoursTextBox.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int hours) ||
            !int.TryParse(
                TimerMinutesTextBox.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minutes) ||
            !int.TryParse(
                TimerSecondsTextBox.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int seconds) ||
            hours > 9999 ||
            minutes > 59 ||
            seconds > 59 ||
            (hours == 0 && minutes == 0 && seconds == 0))
        {
            SetStatus(
                "Enter a valid countdown time: hours, minutes (0-59), and seconds (0-59); the total must be greater than zero.");
            return;
        }

        try
        {
            UpdateConfigValues(new Dictionary<string, string>
            {
                ["default-countdown-time"] =
                    $"{hours}:{minutes:00}:{seconds:00}"
            });
            MainWindow.ReloadConfigFromDisk();
            LoadTimerSettings(ConfigReder.GetConfig());
            SetStatus("Default countdown time saved.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save countdown time: " + ex.Message);
        }
    }

    private void TimerCommandCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (loading)
            return;

        try
        {
            UpdateConfigValues(new Dictionary<string, string>
            {
                ["timer-start-command-enabled"] =
                    TimerStartCommandCheckBox.IsChecked == true ? "true" : "false",
                ["timer-countdown-command-enabled"] =
                    TimerCountdownCommandCheckBox.IsChecked == true ? "true" : "false",
                ["timer-pause-command-enabled"] =
                    TimerPauseCommandCheckBox.IsChecked == true ? "true" : "false",
                ["timer-reset-command-enabled"] =
                    TimerResetCommandCheckBox.IsChecked == true ? "true" : "false",
                ["timer-close-command-enabled"] =
                    TimerCloseCommandCheckBox.IsChecked == true ? "true" : "false"
            });
            MainWindow.ReloadConfigFromDisk();
            SetStatus("Timer command settings saved.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save timer command settings: " + ex.Message);
        }
    }

    private void AutoSaveManagerSettings()
    {
        if (loading)
            return;

        try
        {
            ManagerSettingsData settings = ReadManagerSettings();

            ManagerSettingsStore.Save(settings);
            ApplyStartupSetting(settings.StartOnStartup);

            TimerSound.Configure(
                settings.SoundEnabled,
                settings.SoundVolume);

            DuckyManager.ApplySettings(settings);

            SetStatus("Settings saved automatically.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save settings: " + ex.Message);
        }
    }

    private ManagerSettingsData ReadManagerSettings()
    {
        return new ManagerSettingsData
        {
            Theme = ThemeComboBox.SelectedItem is ThemeData theme
                ? theme.Id
                : ThemeService.GetTheme(null)?.Id ?? "",

            UiScale = ParsePercentage(UIScaleComboBox.SelectedItem),
            SidebarBehavior = SidebarBehaviorComboBox.SelectedItem is ComboBoxItem sidebar
                ? sidebar.Content?.ToString() ?? "Expanded"
                : "Expanded",
            SidebarOrder = SidebarOrderList.Items
                .OfType<ListBoxItem>()
                .Select(item => item.Content?.ToString() ?? "")
                .Where(value => value.Length > 0)
                .ToList(),
            StartOnStartup = StartOnStartupCheckBox.IsChecked == true,
            OpenManagerOnStartup = OpenManagerOnStartupCheckBox.IsChecked == true,
            ConfirmBeforeClosing = ConfirmBeforeClosingCheckBox.IsChecked == true,
            UpdateNotifications = UpdateNotificationsCheckBox.IsChecked == true,
            SoundEnabled = SoundEnabledCheckBox.IsChecked == true,
            SoundVolume = (int)Math.Clamp(SoundVolumeSlider.Value, 0, 100)
        };
    }

    private static int ParsePercentage(object? selectedItem)
    {
        string value = selectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? ""
            : "";
        return int.TryParse(value.TrimEnd('%'), out int percentage)
            ? percentage
            : 100;
    }

    private void ApplyStartupSetting(bool enabled)
    {
        const string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "Ducky";

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath);

        if(enabled)
        {
            string? executable = Environment.ProcessPath;

            if(string.IsNullOrWhiteSpace(executable))
                return;

            key.SetValue(appName, $"\"{executable}\"");
        }
        else
        {
            key.DeleteValue(appName, false);
        }
    }

    private void MoveSidebarUp_Click(object? sender, RoutedEventArgs e)
    {
        int index = SidebarOrderList.SelectedIndex;
        if (index <= 0)
            return;
        MoveSidebarItem(index, index - 1);
    }

    private void MoveSidebarDown_Click(object? sender, RoutedEventArgs e)
    {
        int index = SidebarOrderList.SelectedIndex;
        if (index < 0 || index >= SidebarOrderList.ItemCount - 1)
            return;
        MoveSidebarItem(index, index + 1);
    }

    private void MoveSidebarItem(int from, int to)
    {
        string item = SidebarOrderList.Items[from] is ListBoxItem listItem
            ? listItem.Content?.ToString() ?? ""
            : "";

        SidebarOrderList.Items.RemoveAt(from);

        SidebarOrderList.Items.Insert(to, new ListBoxItem
        {
            Content = item
        });

        SidebarOrderList.SelectedIndex = to;
        SidebarOrderList.ScrollIntoView(SidebarOrderList.SelectedItem);
        AutoSaveManagerSettings();
    }

    private void ResetSidebarOrder_Click(object? sender, RoutedEventArgs e)
    {
        ManagerSettingsData defaults = new();
        SidebarOrderList.Items.Clear();
        foreach (string item in defaults.SidebarOrder)
            SidebarOrderList.Items.Add(new ListBoxItem { Content = item });
        SidebarOrderList.SelectedIndex = 0;
        AutoSaveManagerSettings();
    }

    private async void ChangeConfigLocation_Click(object? sender, RoutedEventArgs e)
    {
        await ChangeLocationAsync(
            DuckyPaths.ConfigFilePath, "ducky.conf", DuckyPaths.SetConfigFilePath,
            path => ConfigLocationTextBox.Text = path, "configuration");
        MainWindow.RebindConfigWatcher();
        MainWindow.ReloadConfigFromDisk();
        ReloadConfigControls();
    }

    private async void ChangeWorkspaceLocation_Click(object? sender, RoutedEventArgs e)
    {
        MainWindow.SaveWorkspaceState();
        await ChangeLocationAsync(
            DuckyPaths.WorkspaceStatePath, "workspaces.json",
            DuckyPaths.SetWorkspaceStatePath,
            path => WorkspaceLocationTextBox.Text = path, "workspace");
    }

    private async void ChangeLogsLocation_Click(object? sender, RoutedEventArgs e)
    {
        await ChangeLocationAsync(
            DuckyPaths.LogFilePath, "log.txt", DuckyPaths.SetLogFilePath,
            path => LogsLocationTextBox.Text = path, "logs");
    }

    private async Task ChangeLocationAsync(
        string currentPath,
        string fileName,
        Action<string> savePath,
        Action<string> setDisplay,
        string description)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        IReadOnlyList<IStorageFolder> folders =
            await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = $"Choose {description} folder",
                    AllowMultiple = false
                });
        if (folders.Count == 0)
            return;

        string destination = Path.GetFullPath(
            Path.Combine(folders[0].Path.LocalPath, fileName));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (!string.Equals(
                    Path.GetFullPath(currentPath),
                    destination,
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(currentPath))
            {
                if (File.Exists(destination))
                    throw new IOException(
                        $"A file already exists at the selected location: {destination}");
                File.Move(currentPath, destination);
            }

            savePath(destination);
            setDisplay(destination);
            SetStatus($"Moved {description} data to the new location.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not move {description} data: {ex.Message}");
        }
    }

    private async void ExportConfig_Click(object? sender, RoutedEventArgs e) =>
        await ExportFileAsync(DuckyPaths.ConfigFilePath, "Ducky configuration");

    private async void ImportConfig_Click(object? sender, RoutedEventArgs e)
    {
        if (await ImportFileAsync(DuckyPaths.ConfigFilePath, "Ducky configuration"))
        {
            MainWindow.RebindConfigWatcher();
            MainWindow.ReloadConfigFromDisk();
            ReloadConfigControls();
        }
    }

    private async void ExportWorkspaces_Click(object? sender, RoutedEventArgs e)
    {
        MainWindow.SaveWorkspaceState();
        await ExportFileAsync(DuckyPaths.WorkspaceStatePath, "Workspace data");
    }

    private async void ImportWorkspaces_Click(object? sender, RoutedEventArgs e)
    {
        if (await ImportFileAsync(DuckyPaths.WorkspaceStatePath, "Workspace data"))
            MainWindow.ReloadWorkspaceState();
    }

    private async void ExportLogs_Click(object? sender, RoutedEventArgs e) =>
        await ExportFileAsync(DuckyPaths.LogFilePath, "Ducky logs");

    private async void ImportLogs_Click(object? sender, RoutedEventArgs e) =>
        await ImportFileAsync(DuckyPaths.LogFilePath, "Ducky logs");

    private async void CleanLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Clear the Ducky log file?"))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DuckyPaths.LogFilePath)!);
            File.WriteAllText(DuckyPaths.LogFilePath, "");
            SetStatus("Logs cleared.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not clear logs: " + ex.Message);
        }
    }

    private async void CleanWorkspaces_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync(
                "Remove saved workspaces and create the default workspace?"))
            return;
        MainWindow.ResetWorkspaceData();
        SetStatus("Workspaces reset to the default state.");
    }

    private async void ResetConfig_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync(
                "Reset the Ducky terminal configuration to its default?"))
            return;
        try
        {
            File.Delete(DuckyPaths.ConfigFilePath);
            _ = DuckyPaths.ConfigFilePath;
            MainWindow.RebindConfigWatcher();
            MainWindow.ReloadConfigFromDisk();
            ReloadConfigControls();
            SetStatus("Configuration reset.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not reset configuration: " + ex.Message);
        }
    }

    private async void ResetManagerSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Reset Manager settings to their defaults?"))
            return;
        try
        {
            ManagerSettingsData defaults = new();
            ManagerSettingsStore.Save(defaults);
            ApplyStartupSetting(defaults.StartOnStartup);

            loading = true;
            LoadManagerSettings(defaults);
            loading = false;

            TimerSound.Configure(defaults.SoundEnabled, defaults.SoundVolume);
            DuckyManager.ApplySettings(defaults);
        }
        catch (Exception ex)
        {
            SetStatus("Could not reset Manager settings: " + ex.Message);
        }
    }

    private async void ExportAllData_Click(object? sender, RoutedEventArgs e)
    {
        MainWindow.SaveWorkspaceState();
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export all Ducky data",
                SuggestedFileName = "ducky-backup.zip",
                FileTypeChoices =
                [
                    new FilePickerFileType("Ducky backup") { Patterns = ["*.zip"] }
                ]
            });
        if (file is null)
            return;

        try
        {
            using var stream = File.Create(file.Path.LocalPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            AddArchiveFile(archive, "config/ducky.conf", DuckyPaths.ConfigFilePath);
            AddArchiveFile(archive, "workspaces/workspaces.json", DuckyPaths.WorkspaceStatePath);
            AddArchiveFile(archive, "themes/themes.json", DuckyPaths.ThemesFilePath);
            AddThemeFilesToArchive(archive);
            AddArchiveFile(archive, "shells/shells.json", ShellRegistry.FilePath);
            AddArchiveFile(archive, "logs/log.txt", DuckyPaths.LogFilePath);
            AddArchiveFile(archive, "manager/settings.json", ManagerSettingsStore.FilePath);
            AddArchiveFile(
                archive,
                "storage-paths.json",
                DuckyPaths.StoragePathSettingsFilePath);
            SetStatus("All Ducky data exported.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not export Ducky data: " + ex.Message);
        }
    }

    private async void ImportAllData_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        IReadOnlyList<IStorageFile> files =
            await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import Ducky backup",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Ducky backup") { Patterns = ["*.zip"] }
                    ]
                });
        if (files.Count == 0 ||
            !await ConfirmAsync("Replace Ducky data with the selected backup?"))
            return;

        try
        {
            using var archive = ZipFile.OpenRead(files[0].Path.LocalPath);

            ZipArchiveEntry? storagePathsEntry =
                archive.GetEntry("storage-paths.json");
            if (storagePathsEntry is not null)
            {
                if (storagePathsEntry.Length > 1024 * 1024)
                    throw new InvalidDataException(
                        "The backup storage-location data is too large.");

                using Stream storageStream = storagePathsEntry.Open();
                DuckyPaths.ImportStoragePathSettings(storageStream);
            }

            Dictionary<string, string> targets = new(StringComparer.Ordinal)
            {
                ["config/ducky.conf"] = DuckyPaths.ConfigFilePath,
                ["workspaces/workspaces.json"] = DuckyPaths.WorkspaceStatePath,
                ["themes/themes.json"] = DuckyPaths.ThemesFilePath,
                ["shells/shells.json"] = ShellRegistry.FilePath,
                ["logs/log.txt"] = DuckyPaths.LogFilePath,
                ["manager/settings.json"] = ManagerSettingsStore.FilePath
            };

            bool themeFilesCleared = false;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                const string themeEntryPrefix = "themes/items/";
                if (entry.FullName.StartsWith(
                        themeEntryPrefix,
                        StringComparison.Ordinal))
                {
                    if (entry.Length > 20 * 1024 * 1024)
                        throw new InvalidDataException(
                            $"Theme file is too large: {entry.FullName}");

                    string relativePath = entry.FullName[themeEntryPrefix.Length..]
                        .Replace('/', Path.DirectorySeparatorChar);
                    string themeRoot = Path.GetFullPath(DuckyPaths.ThemesDirectory);
                    string themePath = Path.GetFullPath(
                        Path.Combine(themeRoot, relativePath));
                    if (!themePath.StartsWith(
                            themeRoot + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Invalid theme file path: {entry.FullName}");
                    }

                    if (!themeFilesCleared)
                    {
                        ClearThemeFiles(themeRoot);
                        themeFilesCleared = true;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(themePath)!);
                    using Stream themeSource = entry.Open();
                    using FileStream themeDestination = File.Create(themePath);
                    await themeSource.CopyToAsync(themeDestination);
                    continue;
                }

                if (!targets.TryGetValue(entry.FullName, out string? target))
                    continue;
                if (entry.Length > 100 * 1024 * 1024)
                    throw new InvalidDataException(
                        $"Backup item is too large: {entry.FullName}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream source = entry.Open();
                using FileStream destination = File.Create(target);
                await source.CopyToAsync(destination);
            }

            MainWindow.RebindConfigWatcher();
            MainWindow.ReloadConfigFromDisk();
            MainWindow.ReloadWorkspaceState();
            ThemeService.NotifyDataReloaded();

            ManagerSettingsData importedSettings = ManagerSettingsStore.Load();
            ApplyStartupSetting(importedSettings.StartOnStartup);
            TimerSound.Configure(
                importedSettings.SoundEnabled,
                importedSettings.SoundVolume);
            DuckyManager.ApplySettings(importedSettings);
            ReloadSettingsFromDisk();

            SetStatus("All Ducky data imported.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not import Ducky data: " + ex.Message);
        }
    }

    private async Task ExportFileAsync(string sourcePath, string title)
    {
        if (!File.Exists(sourcePath))
        {
            SetStatus($"No {title.ToLowerInvariant()} file exists to export.");
            return;
        }
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export " + title,
                SuggestedFileName = Path.GetFileName(sourcePath)
            });
        if (file is null)
            return;
        try
        {
            File.Copy(sourcePath, file.Path.LocalPath, true);
            SetStatus(title + " exported.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not export " + title + ": " + ex.Message);
        }
    }

    private async Task<bool> ImportFileAsync(string targetPath, string title)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return false;
        IReadOnlyList<IStorageFile> files =
            await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import " + title,
                    AllowMultiple = false
                });
        if (files.Count == 0 ||
            !await ConfirmAsync($"Replace the current {title.ToLowerInvariant()} data?"))
            return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(files[0].Path.LocalPath, targetPath, true);
            SetStatus(title + " imported.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("Could not import " + title + ": " + ex.Message);
            return false;
        }
    }

    private static void AddArchiveFile(
        ZipArchive archive,
        string entryName,
        string path)
    {
        if (File.Exists(path))
            archive.CreateEntryFromFile(path, entryName);
    }

    private static void AddThemeFilesToArchive(ZipArchive archive)
    {
        if (!Directory.Exists(DuckyPaths.ThemesDirectory))
            return;

        foreach (string path in Directory.EnumerateFiles(
                     DuckyPaths.ThemesDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(
                    DuckyPaths.ThemesDirectory,
                    path)
                .Replace('\\', '/');
            archive.CreateEntryFromFile(
                path,
                "themes/items/" + relativePath);
        }
    }

    private static void ClearThemeFiles(string themeRoot)
    {
        if (!Directory.Exists(themeRoot))
            return;

        foreach (string file in Directory.EnumerateFiles(themeRoot))
            File.Delete(file);
        foreach (string directory in Directory.EnumerateDirectories(themeRoot))
            Directory.Delete(directory, recursive: true);
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        Window? dialog = null;
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var confirm = new Button { Content = "Continue", MinWidth = 80 };
        cancel.Click += (_, _) => dialog?.Close(false);
        confirm.Click += (_, _) => dialog?.Close(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        dialog = new Window
        {
            Title = "Confirm",
            Width = 420,
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    buttons
                }
            }
        };
        return await dialog.ShowDialog<bool>(owner);
    }

    private void SetStatus(string message) => SettingsStatusText.Text = message;

    private void CheckForUpdates_Click(object? sender, RoutedEventArgs e) { }

    private void SendFeedback_Click(object? sender, RoutedEventArgs e) { }
    private void OpenGitHub_Click(object? sender, RoutedEventArgs e) { }
    private void OpenLicense_Click(object? sender, RoutedEventArgs e) { }
    private void OpenCredits_Click(object? sender, RoutedEventArgs e) { }

    private void ResetChanges_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            loading = true;
            LoadManagerSettings(ManagerSettingsStore.Load());
            Config config = ConfigReder.GetConfig();
            ShowNavbarCheckBox.IsChecked = config.ShowNavbar;
            string? shellId = ShellRegistry.Resolve(config.Shell)?.Id;
            DefaultShellComboBox.SelectedItem = shellProfiles.FirstOrDefault(
                profile => profile.Id == shellId);
            SetStatus("Unsaved changes were discarded.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not reload settings: " + ex.Message);
        }
        finally
        {
            loading = false;
        }
    }
}
