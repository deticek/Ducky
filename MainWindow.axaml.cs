using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ducky;

public partial class MainWindow : Window
{

    //workspace code
    private WorkspaceSessionState workspaceState = new();
    private readonly List<WorkspaceLayout> workspaces = new();
    private WorkspaceLayout? temporaryWorkspace;
    private int workspaceMax;
    private int activeWorkspaceIndex = -1;

    private readonly Grid temporaryTerminalHost = new() { IsVisible = false };
    private ConfigHotReloader configHotReloader = null!;
    private Config currentConfig = ConfigReder.GetConfig();
    private readonly DispatcherTimer workspaceInfoTimer;
    private bool isClosing;
    private bool customCommandStartupTriggered;
    private bool customCommandExitRunning;
    private bool customCommandExitCompleted;
    private bool preserveTemporaryWorkspaceState;
    private KeyListener _keyListener = null!;
    private static MainWindow? _instance;
    
    //timer
    private readonly TimerService timerService = new();
    private TimerWindow? timerWindow;
    private DuckyManager? managerWindow;
    private readonly DispatcherTimer clockTimer = new();
    private bool timerFinished = false;

    private bool skipWorkspaceSave = false;

    //commands
    public static CommandManager Commands { get; } = new CommandManager();

    public MainWindow()
    {
        InitializeComponent();
        ManagerSettingsData managerSettings = ManagerSettingsStore.Load();
        TimerSound.Configure(
            managerSettings.SoundEnabled,
            managerSettings.SoundVolume);

        Commands.Register(new TestCommand());
        Commands.Register(new TimerStartCommand());
        Commands.Register(new TimerCountdownCommand());
        Commands.Register(new TimerPauseCommand());
        Commands.Register(new TimerResetCommand());
        Commands.Register(new TimerCloseCommand());
        Commands.Register(new WorkspaceResetCommand());

        WorkspaceHost.Children.Add(temporaryTerminalHost);
        Navbar.IsVisible = currentConfig.ShowNavbar;
        _instance = this;
        ThemeService.Changed += OnThemesChanged;

        workspaceMax = currentConfig.WorkspaceMax;
        workspaceInfoTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        workspaceInfoTimer.Tick += (_, _) =>
        {
            workspaceInfoTimer.Stop();
            WorkspaceInfoBox.IsVisible = false;
        };

        timerService.StateChanged += state =>
        {
            TimerButton.IsVisible = true;

            if (!state.IsActive)
            {
                if (!timerFinished)
                    TimerText.Text = DateTime.Now.ToString("HH:mm");

                return;
            }

            TimeSpan time = state.Time;

            if (time.TotalHours >= 1)
                TimerText.Text = time.ToString(@"hh\:mm\:ss");
            else
                TimerText.Text = time.ToString(@"mm\:ss");
        };

        TimerButton.Click += (_, _) =>
        {
            OpenTimerWindow();
        };

        timerService.CountdownCompleted += async () =>
        {
            timerFinished = true;

            TimerSound.PlayBell();
            //OpenTimerWindow();

            await Task.Delay(1000);

            timerFinished = false;
            TimerText.Text = DateTime.Now.ToString("HH:mm");
        };

        clockTimer.Interval = TimeSpan.FromSeconds(1);
        clockTimer.Tick += (_, _) =>
        {
            if (!timerService.State.IsActive && !timerFinished)
            {
                TimerText.Text = DateTime.Now.ToString("HH:mm");
            }
        };

        clockTimer.Start();

        TimerText.Text = DateTime.Now.ToString("HH:mm");

        //timerService.StartCountdown(TimeSpan.FromMinutes(10));

        _keyListener = new KeyListener(() =>
        {
            if (temporaryTerminalHost.IsVisible)
                return temporaryWorkspace?.ActiveTerminal;

            if (activeWorkspaceIndex < 0 ||
                activeWorkspaceIndex >= workspaces.Count)
                return null;

            return workspaces[activeWorkspaceIndex].ActiveTerminal;
        }, currentConfig);
        configHotReloader = new ConfigHotReloader(currentConfig);
        configHotReloader.ConfigChanged += ApplyConfig;

        AddHandler(
            KeyDownEvent,
            _keyListener.OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        AddHandler(
            KeyUpEvent,
            _keyListener.OnKeyUp,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            SaveWorkspaces();
            isClosing = true;

            foreach (WorkspaceLayout workspace in workspaces)
                workspace.Shutdown();

            temporaryWorkspace?.Shutdown();
            configHotReloader.Dispose();
            workspaceInfoTimer.Stop();
            ThemeService.Changed -= OnThemesChanged;
        };
        Opened += (_, _) => FocusVisibleTerminal();
        Activated += (_, _) => FocusVisibleTerminal();

        WorkspaceSessionState? savedState = LoadWorkspaceState();
        if (savedState is not null)
            workspaceState = savedState;

        for (int index = 0; index < workspaceMax; index++)
        {
            WorkspaceLayout workspace = CreateWorkspace();
            workspaces.Add(workspace);
            WorkspaceHost.Children.Add(workspace.View);
        }

        if (!WorkspaceHost.Children.Contains(temporaryTerminalHost))
            WorkspaceHost.Children.Add(temporaryTerminalHost);

        int restoredActiveIndex = savedState?.ActiveWorkspaceIndex ?? 0;
        restoredActiveIndex = Math.Clamp(restoredActiveIndex, 0, workspaces.Count - 1);
        SwitchToWorkspace(restoredActiveIndex);

        if (Program.StartupWorkingDirectory is string workingDirectory)
        {
            preserveTemporaryWorkspaceState = true;
            OpenTemporaryTerminalInstance(workingDirectory);
        }
        else if (currentConfig.AlwaysOpenInTemporaryTerminal)
        {
            OpenTemporaryTerminalInstance();
        }

        RunStartupCustomCommands();
    }

    public static async Task RunOnUiThread(Action action)
    {
        if(Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }
    
        await Dispatcher.UIThread.InvokeAsync(action);
    }
    
    public static void ResetTimerDisplay()
    {
        if (_instance == null)
            return;

        _instance.timerFinished = false;
        _instance.TimerText.Text = DateTime.Now.ToString("HH:mm");
    }

    public static void TimerStart()
    {
        if (_instance == null)
            return;

        ResetTimerDisplay();
        _instance.timerService.StartStopwatch();
    }

    public static void CountdownStart()
    {
        if (_instance == null)
            return;

        ResetTimerDisplay();
        _instance.timerService.StartCountdown(
            _instance.currentConfig.DefaultCountdownTime);
    }

    public static void TimerPause()
    {
        if (_instance == null)
            return;

        if (_instance.timerService.State.IsPaused)
            _instance.timerService.Resume();
        else
            _instance.timerService.Pause();
    }

    public static void TimerReset()
    {
        if (_instance == null)
            return;

        _instance.timerService.Reset(
            _instance.currentConfig.DefaultCountdownTime);

        ResetTimerDisplay();
    }

    public static void CloseTimerWindow()
    {
        _instance?.timerWindow?.Close();
    }

    private WorkspaceLayout CreateWorkspace()
    {
        return new WorkspaceLayout();
    }

    private void InitializeWorkspace(int index)
    {
        WorkspaceLayout workspace = workspaces[index];
        if (workspace.IsInitialized)
            return;

        WorkspaceData? data = index < workspaceState.Workspaces.Count
            ? workspaceState.Workspaces[index]
            : null;
        workspace.Theme = ThemeService.GetTheme(data?.ThemeId);

        if (data?.Root is not null)
        {
            List<TerminalView> partiallyRestoredTerminals = new();
            try
            {
                workspace.Restore(
                    new WorkspaceLayoutState
                    {
                        Root = data.Root,
                        ActivePaneIndex = data.ActivePaneIndex
                    },
                    pane =>
                    {
                        ShellProfile? shellProfile = pane.Shell is not null
                            ? ShellRegistry.Resolve(pane.Shell)
                            : null;
                        shellProfile ??= ShellRegistry.Resolve(data.DefaultShell);
                        TerminalView terminal = CreateTerminal(
                            workspace,
                            pane.WorkingDirectory,
                            shellProfile: shellProfile);
                        partiallyRestoredTerminals.Add(terminal);
                        return terminal;
                    });
                return;
            }
            catch (Exception ex)
            {
                DuckyLog.Log($"Could not restore workspace {index + 1}: " + ex);
                foreach (TerminalView terminal in partiallyRestoredTerminals)
                    terminal.Shutdown();
                workspace.View.Children.Clear();
            }
        }

        TerminalView initialTerminal = CreateTerminal(
            workspace,
            null,
            shellProfile: ShellRegistry.Resolve(data?.DefaultShell));
        workspace.Initialize(initialTerminal);
    }

    private void OnThemesChanged()
    {
        ApplyWorkspaceThemes(workspaceState);
    }

    internal static void ApplyWorkspaceThemes(WorkspaceSessionState updatedState)
    {
        if (_instance is null)
            return;

        _instance.workspaceState = updatedState;
        _instance.ApplyWorkspaceThemes();
    }

    internal static void ApplyWorkspaceState(WorkspaceSessionState updatedState)
    {
        if (_instance is null)
            return;

        for (int index = 0;
             index < Math.Min(_instance.workspaces.Count, updatedState.Workspaces.Count);
             index++)
        {
            WorkspaceLayout layout = _instance.workspaces[index];
            WorkspaceData data = updatedState.Workspaces[index];
            if (!layout.IsInitialized || data.Root is null)
                continue;

            List<WorkspacePaneState> panes = GetWorkspaceLeaves(data.Root);
            IReadOnlyList<TerminalView> terminals = layout.Terminals;
            for (int paneIndex = 0;
                 paneIndex < Math.Min(panes.Count, terminals.Count);
                 paneIndex++)
            {
                string? shellValue = panes[paneIndex].Shell ?? data.DefaultShell;
                ShellProfile? profile = ShellRegistry.Resolve(shellValue);
                if (profile is not null &&
                    terminals[paneIndex].ShellProfileId != profile.Id)
                {
                    terminals[paneIndex].SwitchShell(profile);
                }
            }
        }

        if (_instance.temporaryWorkspace?.IsInitialized == true &&
            updatedState.TemporaryWorkspace.Root is not null)
        {
            List<WorkspacePaneState> panes = GetWorkspaceLeaves(
                updatedState.TemporaryWorkspace.Root);
            IReadOnlyList<TerminalView> terminals =
                _instance.temporaryWorkspace.Terminals;
            for (int index = 0; index < Math.Min(panes.Count, terminals.Count); index++)
            {
                ShellProfile? profile = ShellRegistry.Resolve(
                    panes[index].Shell ?? updatedState.TemporaryWorkspace.DefaultShell);
                if (profile is not null &&
                    terminals[index].ShellProfileId != profile.Id)
                {
                    terminals[index].SwitchShell(profile);
                }
            }
        }

        _instance.workspaceState = updatedState;
        _instance.UpdateActiveWorkspaceLabel();
    }

    private static List<WorkspacePaneState> GetWorkspaceLeaves(
        WorkspacePaneState root)
    {
        List<WorkspacePaneState> leaves = new();
        AddLeaves(root, leaves);
        return leaves;
    }

    private static void AddLeaves(
        WorkspacePaneState pane,
        List<WorkspacePaneState> leaves)
    {
        if (pane.Orientation is null)
        {
            leaves.Add(pane);
            return;
        }

        if (pane.First is not null)
            AddLeaves(pane.First, leaves);
        if (pane.Second is not null)
            AddLeaves(pane.Second, leaves);
    }

    private void ApplyWorkspaceThemes()
    {
        List<ThemeData> themes = ThemeService.LoadAll();
        ThemeData? defaultTheme = ThemeService.GetTheme(null);

        for (int index = 0; index < workspaces.Count; index++)
        {
            string? themeId = index < workspaceState.Workspaces.Count
                ? workspaceState.Workspaces[index].ThemeId
                : null;
            ThemeData? theme = string.IsNullOrWhiteSpace(themeId)
                ? defaultTheme
                : themes.FirstOrDefault(item => item.Id == themeId) ?? defaultTheme;

            WorkspaceLayout workspace = workspaces[index];
            workspace.Theme = theme;
            foreach (TerminalView terminal in workspace.Terminals)
                terminal.ApplyTheme(theme);
        }

        if (temporaryWorkspace is not null)
        {
            ThemeData? temporaryTheme = string.IsNullOrWhiteSpace(
                workspaceState.TemporaryWorkspace.ThemeId)
                ? defaultTheme
                : themes.FirstOrDefault(
                    item => item.Id == workspaceState.TemporaryWorkspace.ThemeId) ??
                  defaultTheme;
            temporaryWorkspace.Theme = temporaryTheme;
            foreach (TerminalView terminal in temporaryWorkspace.Terminals)
                terminal.ApplyTheme(temporaryTheme);
        }

        ThemeData? chromeTheme = temporaryTerminalHost.IsVisible
            ? temporaryWorkspace?.Theme
            : activeWorkspaceIndex >= 0 && activeWorkspaceIndex < workspaces.Count
                ? workspaces[activeWorkspaceIndex].Theme
                : defaultTheme;
        ApplyNavbarTheme(chromeTheme ?? defaultTheme);
    }

    private void ApplyNavbarTheme(ThemeData? theme)
    {
        if (theme is null)
            return;

        Color background = ParseThemeColor(theme.NavbarBackgroundColor, "#202020");
        Color button = ParseThemeColor(theme.NavbarButtonColor, "#F4D03F");
        Color buttonText = ParseThemeColor(theme.NavbarButtonTextColor, "#202020");

        Navbar.Background = new SolidColorBrush(background);
        NavbarTitleText.Foreground =
            new SolidColorBrush(ParseThemeColor(
                theme.NavbarTitleTextColor,
                "#FFFFFF"));
        FileMenuBorder.Background = new SolidColorBrush(button);
        FileMenuRoot.Background = new SolidColorBrush(button);
        FileMenuRoot.Foreground = new SolidColorBrush(buttonText);

        foreach (Button navbarButton in new[] { ManagerButton, TimerButton })
        {
            navbarButton.Background = new SolidColorBrush(button);
            navbarButton.Foreground = new SolidColorBrush(buttonText);
        }

        WorkspaceLabelBorder.Background =
            new SolidColorBrush(ParseThemeColor(
                theme.WorkspaceLabelBackgroundColor,
                "#11111B"));
        WorkspaceLabelBorder.BorderBrush =
            new SolidColorBrush(button);
        ActiveWorkspaceLabel.Foreground =
            new SolidColorBrush(ParseThemeColor(
                theme.WorkspaceLabelTextColor,
                "#FFFFFF"));
        WorkspaceInfoBox.Background = new SolidColorBrush(button);
        WorkspaceInfoBox.BorderBrush = new SolidColorBrush(background);
        WorkspaceInfoText.Foreground = new SolidColorBrush(buttonText);
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        HideFileMenu();
        IStorageProvider? storageProvider =
            TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return;

        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await storageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Open folder",
                    AllowMultiple = false
                });
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not browse for terminal folder: " + ex);
            await ShowFileActionMessage("Could not open folder picker", ex.Message);
            return;
        }

        if (folders.Count == 0)
            return;

        try
        {
            GetActiveLayout()?.ActiveTerminal?.ChangeDirectory(
                folders[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not open selected terminal folder: " + ex);
            await ShowFileActionMessage("Could not open folder", ex.Message);
        }
    }

    private async void ExportText_Click(object? sender, RoutedEventArgs e)
    {
        HideFileMenu();
        TerminalView? terminal = GetActiveLayout()?.ActiveTerminal;
        if (terminal is null)
            return;

        IStorageProvider? storageProvider =
            TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return;

        IStorageFile? file;
        try
        {
            file = await storageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export terminal text",
                    SuggestedFileName = "ducky-terminal.txt",
                    DefaultExtension = "txt",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Text files")
                        {
                            Patterns = ["*.txt"]
                        }
                    ]
                });
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not open terminal text export picker: " + ex);
            await ShowFileActionMessage("Could not export text", ex.Message);
            return;
        }

        if (file is null)
            return;

        try
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, terminal.GetAllText());
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not export terminal text: " + ex);
            await ShowFileActionMessage("Could not export text", ex.Message);
        }
    }

    private async void Find_Click(object? sender, RoutedEventArgs e)
    {
        HideFileMenu();
        TerminalView? terminal = GetActiveLayout()?.ActiveTerminal;
        if (terminal is null)
            return;

        TextBox input = new()
        {
            PlaceholderText = "Find in terminal",
            MinWidth = 300,
            Margin = new Thickness(12)
        };
        Button findButton = new()
        {
            Content = "Find",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 12, 12)
        };
        Window dialog = new()
        {
            Title = "Find",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Children = { input, findButton }
            }
        };

        findButton.Click += (_, _) => dialog.Close(input.Text ?? "");
        input.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
                dialog.Close(input.Text ?? "");
        };

        string? query = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(query) && !terminal.FindText(query))
            await ShowFileActionMessage(
                "No match",
                $"No terminal text found for \"{query}\".");
    }

    private async Task ShowFileActionMessage(string title, string message)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
                    }
                }
            }
        };
        if (dialog.Content is StackPanel panel && panel.Children[1] is Button button)
            button.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }

    private void OpenManager_Click(object? sender, RoutedEventArgs e)
    {
        HideFileMenu();
        ShowDuckyManager();
    }

    private void CloseSplit_Click(object? sender, RoutedEventArgs e)
    {
        HideFileMenu();
        CloseFocusedPane();
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        HideFileMenu();
        Close();
    }

    public static void OpenFileMenu()
    {
        if (_instance == null)
            return;

        bool isOpen = _instance.FileMenuRoot.IsSubMenuOpen;
        _instance.FileMenuRoot.IsSubMenuOpen = !isOpen;
    }

    public static void CloseFileMenu()
    {
        if (_instance == null)
            return;

        _instance.FileMenuRoot.IsSubMenuOpen = false;
    }

    private void HideFileMenu()
    {
        CloseFileMenu();
    }

    private static Color ParseThemeColor(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out Color color)
            ? color
            : Color.Parse(fallback);

    public static void ReloadConfigFromDisk()
    {
        if (_instance is null)
            return;

        _instance.ApplyConfig(ConfigReder.GetConfig());
    }

    public static void RebindConfigWatcher()
    {
        if (_instance is null)
            return;

        _instance.configHotReloader.ConfigChanged -= _instance.ApplyConfig;
        _instance.configHotReloader.Dispose();
        _instance.configHotReloader =
            new ConfigHotReloader(_instance.currentConfig);
        _instance.configHotReloader.ConfigChanged += _instance.ApplyConfig;
    }

    public static void SaveWorkspaceState() => _instance?.SaveWorkspaces();

    private void ApplyConfig(Config updatedConfig)
    {
        try
        {
            bool wasAlwaysOpenInTemporary =
                currentConfig.AlwaysOpenInTemporaryTerminal;
            bool wasTemporaryActive = temporaryTerminalHost.IsVisible;
            currentConfig = updatedConfig;
            if(timerWindow != null)
            {
                timerWindow.UpdateDefaultCountdownTime(updatedConfig.DefaultCountdownTime);
            }
            workspaceMax = updatedConfig.WorkspaceMax;
            Navbar.IsVisible = updatedConfig.ShowNavbar;
            _keyListener.UpdateConfig(updatedConfig);

            foreach (WorkspaceLayout workspace in workspaces)
            {
                foreach (TerminalView terminal in workspace.Terminals)
                    terminal.ApplyConfig(updatedConfig);
            }
            if (temporaryWorkspace is not null)
            {
                foreach (TerminalView terminal in temporaryWorkspace.Terminals)
                    terminal.ApplyConfig(updatedConfig);
            }

            while (workspaces.Count < workspaceMax)
            {
                WorkspaceLayout workspace = CreateWorkspace();
                workspaces.Add(workspace);
                WorkspaceHost.Children.Add(workspace.View);
                workspace.View.IsVisible = workspaces.Count - 1 == activeWorkspaceIndex;
            }

            while (workspaces.Count > workspaceMax)
            {
                int lastIndex = workspaces.Count - 1;
                WorkspaceLayout workspace = workspaces[lastIndex];
                workspace.Shutdown();
                WorkspaceHost.Children.Remove(workspace.View);
                workspaces.RemoveAt(lastIndex);
            }

            if (activeWorkspaceIndex >= workspaces.Count)
            {
                activeWorkspaceIndex = workspaces.Count - 1;
                if (!wasTemporaryActive)
                    SwitchToWorkspace(activeWorkspaceIndex);
            }

            if (!wasAlwaysOpenInTemporary &&
                updatedConfig.AlwaysOpenInTemporaryTerminal)
            {
                OpenTemporaryTerminalInstance();
            }
            else if (wasAlwaysOpenInTemporary &&
                     !updatedConfig.AlwaysOpenInTemporaryTerminal &&
                     temporaryTerminalHost.IsVisible)
            {
                SwitchToWorkspace(Math.Clamp(
                    activeWorkspaceIndex,
                    0,
                    workspaces.Count - 1));
            }

            SaveWorkspaces();
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not apply reloaded config: " + ex);
        }
    }

    private TerminalView CreateTerminal(
        WorkspaceLayout workspace,
        string? workingDirectory,
        bool persistWorkspaceState = true,
        ShellProfile? shellProfile = null)
    {
        shellProfile ??= GetDefaultShellProfile();
        var terminal = new TerminalView(workingDirectory, shellProfile);
        terminal.ApplyTheme(workspace.Theme);
        
        terminal.GotFocus += (_, _) =>
        {
            workspace.ActiveTerminal = terminal;
            if (persistWorkspaceState)
                SaveWorkspaces();
        };
        if (persistWorkspaceState)
            terminal.WorkingDirectoryChanged += _ => SaveWorkspaces();
        terminal.Preload();
        _ = CustomCommandService.RunTriggerAsync(
            CustomCommandTriggers.OnTerminalOpen,
            terminal);
        return terminal;
    }

    private ShellProfile? GetDefaultShellProfile()
    {
        string? configuredShell = temporaryTerminalHost.IsVisible
            ? workspaceState.TemporaryWorkspace.DefaultShell
            : activeWorkspaceIndex >= 0 &&
              activeWorkspaceIndex < workspaceState.Workspaces.Count
                ? workspaceState.Workspaces[activeWorkspaceIndex].DefaultShell
                : currentConfig.Shell;
        return ShellRegistry.Resolve(configuredShell);
    }

    public static async void OpenShellPicker()
    {
        if (_instance is null)
            return;

        WorkspaceLayout? layout = _instance.GetActiveLayout();
        TerminalView? terminal = layout?.ActiveTerminal;
        if (terminal is null)
            return;

        try
        {
            var picker = new ShellPickerWindow(ShellRegistry.Load());
            ShellProfile? selected = await picker.ShowDialog<ShellProfile?>(_instance);
            if (selected is not null)
            {
                if (selected.Id != terminal.ShellProfileId)
                    terminal.SwitchShell(selected);

                _instance.SaveWorkspaces();
            }
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not open shell picker: " + ex);
        }
        finally
        {
            _instance._keyListener.ResetModifiers();

            _instance.Activate();
            terminal.Focus();

            Dispatcher.UIThread.Post(
                () => terminal.Focus(),
                DispatcherPriority.Input);
        }
    }

    private WorkspaceSessionState? LoadWorkspaceState()
    {
        try
        {
            return WorkspaceStore.Load();
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not load saved workspaces: " + ex);
            return null;
        }
    }

    private void SaveWorkspaces()
    {
        if (isClosing || workspaces.Count == 0 || skipWorkspaceSave)
            return;

        try
        {
            List<WorkspaceData> savedWorkspaces = new();

            for (int i = 0; i < workspaces.Count; i++)
            {
                WorkspaceData data;

                if (i < workspaceState.Workspaces.Count)
                {
                    data = workspaceState.Workspaces[i];
                }
                else
                {
                    data = new WorkspaceData
                    {
                        Title = "Workspace " + (i + 1),
                        DefaultShell =
                            ShellRegistry.Resolve(currentConfig.Shell)?.Id ??
                            currentConfig.Shell
                    };
                }

                if (workspaces[i].IsInitialized)
                {
                    WorkspaceLayoutState layoutState =
                        workspaces[i].Save(
                            ShellRegistry.Resolve(data.DefaultShell)?.Id);
                    data.Root = layoutState.Root;
                    data.ActivePaneIndex = layoutState.ActivePaneIndex;
                }
                else if (data.Root is null)
                {
                    data.Root = new WorkspacePaneState
                    {
                        WorkingDirectory = Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile)
                    };
                }

                savedWorkspaces.Add(data);
            }

            workspaceState.ActiveWorkspaceIndex = activeWorkspaceIndex;
            workspaceState.Workspaces = savedWorkspaces;
            if (!preserveTemporaryWorkspaceState &&
                temporaryWorkspace?.IsInitialized == true)
            {
                WorkspaceLayoutState temporaryState =
                    temporaryWorkspace.Save(
                        ShellRegistry.Resolve(
                            workspaceState.TemporaryWorkspace.DefaultShell)?.Id);
                workspaceState.TemporaryWorkspace.Root = temporaryState.Root;
                workspaceState.TemporaryWorkspace.ActivePaneIndex =
                    temporaryState.ActivePaneIndex;
                if (temporaryWorkspace.ActiveTerminal is not null)
                {
                    workspaceState.TemporaryWorkspace.WorkingDirectory =
                        temporaryWorkspace.ActiveTerminal.WorkingDirectory;
                }
            }

            WorkspaceStore.Save(workspaceState);
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not save workspaces: " + ex);
        }
    }

    public static void ResetWorkspaceData()
    {
        _instance?.ResetWorkspaces();
    }

    public static void ReloadWorkspaceState()
    {
        MainWindow? window = _instance;
        if (window is null)
            return;

        WorkspaceSessionState? imported = window.LoadWorkspaceState();
        if (imported is null)
            return;

        foreach (WorkspaceLayout workspace in window.workspaces)
        {
            workspace.Shutdown();
            window.WorkspaceHost.Children.Remove(workspace.View);
        }

        window.workspaces.Clear();
        window.workspaceState = imported;
        for (int index = 0; index < window.workspaceMax; index++)
        {
            WorkspaceLayout workspace = window.CreateWorkspace();
            window.workspaces.Add(workspace);
            window.WorkspaceHost.Children.Add(workspace.View);
        }

        int nextIndex = Math.Clamp(
            imported.ActiveWorkspaceIndex,
            0,
            Math.Max(0, window.workspaces.Count - 1));
        window.activeWorkspaceIndex = -1;
        window.SwitchToWorkspace(nextIndex);
    }

    public void ResetWorkspaces()
    {
        try
        {
            skipWorkspaceSave = true;

            foreach (WorkspaceLayout workspace in workspaces)
            {
                foreach (TerminalView terminal in workspace.Terminals)
                {
                    terminal.Shutdown();
                }
            }

            temporaryWorkspace?.Shutdown();

            foreach (WorkspaceLayout workspace in workspaces)
            {
                WorkspaceHost.Children.Remove(workspace.View);
            }

            workspaces.Clear();
            workspaceState.Workspaces.Clear();
            workspaceState.TemporaryWorkspace = new TemporaryWorkspaceState
            {
                DefaultShell =
                    ShellRegistry.Resolve(currentConfig.Shell)?.Id ??
                    currentConfig.Shell
            };

            for (int index = 0; index < workspaceMax; index++)
            {
                WorkspaceLayout workspace = CreateWorkspace();
                workspaces.Add(workspace);
                WorkspaceHost.Children.Add(workspace.View);
            }

            activeWorkspaceIndex = Math.Clamp(
                activeWorkspaceIndex,
                0,
                workspaces.Count - 1);

            skipWorkspaceSave = false;

            SwitchToWorkspace(activeWorkspaceIndex);
        }
        catch (Exception ex)
        {
            skipWorkspaceSave = false;
            DuckyLog.Log("Could not reset workspaces: " + ex);
        }
    }

    private void SwitchToWorkspace(int index)
    {
        if (index < 0 || index >= workspaces.Count)
            return;

        InitializeWorkspace(index);
        activeWorkspaceIndex = index;
        temporaryTerminalHost.IsVisible = false;
        for (int workspaceIndex = 0; workspaceIndex < workspaces.Count; workspaceIndex++)
            workspaces[workspaceIndex].View.IsVisible = workspaceIndex == index;

        Title = $"Ducky - Workspace {index + 1}";
        UpdateActiveWorkspaceLabel();
        ApplyNavbarTheme(workspaces[index].Theme ?? ThemeService.GetTheme(null));
        SaveWorkspaces();
        FocusActivePane();
    }

    private void UpdateActiveWorkspaceLabel()
    {
        if (temporaryTerminalHost.IsVisible)
        {
            ActiveWorkspaceLabel.Text = "Temporary";
            return;
        }

        string fallbackTitle = $"Workspace {activeWorkspaceIndex + 1}";
        ActiveWorkspaceLabel.Text =
            activeWorkspaceIndex >= 0 &&
            activeWorkspaceIndex < workspaceState.Workspaces.Count &&
            !string.IsNullOrWhiteSpace(
                workspaceState.Workspaces[activeWorkspaceIndex].Title)
                ? workspaceState.Workspaces[activeWorkspaceIndex].Title
                : fallbackTitle;
    }

    private void OpenTemporaryTerminalInstance(string? workingDirectory = null)
    {
        temporaryWorkspace?.Shutdown();
        temporaryTerminalHost.Children.Clear();

        var workspace = new WorkspaceLayout();
        ThemeData? defaultTheme = ThemeService.GetTheme(null);
        string? themeId = workspaceState.TemporaryWorkspace.ThemeId;
        workspace.Theme = string.IsNullOrWhiteSpace(themeId)
            ? defaultTheme
            : ThemeService.LoadAll().FirstOrDefault(theme => theme.Id == themeId) ??
              defaultTheme;

        WorkspacePaneState? root = workingDirectory is null
            ? workspaceState.TemporaryWorkspace.Root
            : null;
        if (root is not null)
        {
            List<TerminalView> partiallyRestoredTerminals = new();
            try
            {
                workspace.Restore(
                    new WorkspaceLayoutState
                    {
                        Root = root,
                        ActivePaneIndex =
                            workspaceState.TemporaryWorkspace.ActivePaneIndex
                    },
                    pane =>
                    {
                        ShellProfile? shellProfile = pane.Shell is not null
                            ? ShellRegistry.Resolve(pane.Shell)
                            : null;
                        shellProfile ??= ShellRegistry.Resolve(
                            workspaceState.TemporaryWorkspace.DefaultShell);
                        TerminalView terminal = CreateTerminal(
                            workspace,
                            pane.WorkingDirectory,
                            persistWorkspaceState: false,
                            shellProfile: shellProfile);
                        partiallyRestoredTerminals.Add(terminal);
                        return terminal;
                    });
            }
            catch (Exception ex)
            {
                DuckyLog.Log(
                    "Could not restore temporary workspace: " + ex);
                foreach (TerminalView terminal in partiallyRestoredTerminals)
                    terminal.Shutdown();
                workspace.View.Children.Clear();
            }
        }

        if (!workspace.IsInitialized)
        {
            TerminalView terminal = CreateTerminal(
                workspace,
                workingDirectory ??
                    workspaceState.TemporaryWorkspace.WorkingDirectory,
                persistWorkspaceState: false,
                shellProfile: ShellRegistry.Resolve(
                    workspaceState.TemporaryWorkspace.DefaultShell));
            workspace.Initialize(terminal);
        }

        foreach (TerminalView terminal in workspace.Terminals)
            terminal.AttachedToVisualTree += (_, _) => FocusTemporaryTerminal();

        temporaryWorkspace = workspace;
        temporaryTerminalHost.Children.Add(workspace.View);

        foreach (WorkspaceLayout loadedWorkspace in workspaces)
            loadedWorkspace.View.IsVisible = false;

        temporaryTerminalHost.IsVisible = true;
        Title = "Ducky - Temporary";
        ActiveWorkspaceLabel.Text = "Temporary";
        ApplyNavbarTheme(workspace.Theme);
        if (IsVisible)
            Activate();
        FocusTemporaryTerminal();
    }

    public static void OpenTemporaryTerminal()
    {
        _instance?.OpenTemporaryTerminalInstance();
    }

    private void FocusActivePane()
    {
        int index = activeWorkspaceIndex;
        Dispatcher.UIThread.Post(() =>
        {
            if (temporaryTerminalHost.IsVisible ||
                index != activeWorkspaceIndex ||
                index < 0 ||
                index >= workspaces.Count)
            {
                return;
            }

            WorkspaceLayout workspace = workspaces[index];
            if (!workspace.View.IsEffectivelyVisible)
                return;

            if (!workspace.Contains(workspace.ActiveTerminal))
                workspace.ActiveTerminal = workspace.GetFirstTerminal();

            workspace.ActiveTerminal?.Focus();
        });
    }

    private void FocusVisibleTerminal()
    {
        if (temporaryTerminalHost.IsVisible)
        {
            FocusTemporaryTerminal();
            return;
        }

        int attempts = 0;

        void TryFocusVisibleTerminal()
        {
            if (activeWorkspaceIndex < 0 ||
                activeWorkspaceIndex >= workspaces.Count)
            {
                return;
            }

            WorkspaceLayout workspace = workspaces[activeWorkspaceIndex];
            if (!workspace.Contains(workspace.ActiveTerminal))
                workspace.ActiveTerminal = workspace.GetFirstTerminal();
            TerminalView? terminal = workspace.ActiveTerminal;
            if (terminal is null)
                return;

            if (!terminal.IsEffectivelyVisible || !IsActive)
            {
                if (!IsActive && IsVisible)
                    Activate();

                if (++attempts < 10)
                {
                    DispatcherTimer.RunOnce(
                        TryFocusVisibleTerminal,
                        TimeSpan.FromMilliseconds(50),
                        DispatcherPriority.Input);
                }

                return;
            }

            terminal.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
            if (!terminal.IsFocused && ++attempts < 10)
            {
                DispatcherTimer.RunOnce(
                    TryFocusVisibleTerminal,
                    TimeSpan.FromMilliseconds(50),
                    DispatcherPriority.Input);
            }
        }

        Dispatcher.UIThread.Post(
            TryFocusVisibleTerminal,
            DispatcherPriority.Input);
    }

    private void FocusTemporaryTerminal()
    {
        WorkspaceLayout? targetWorkspace = temporaryWorkspace;
        TerminalView? targetTerminal = targetWorkspace?.ActiveTerminal;
        if (targetWorkspace is null || targetTerminal is null)
            return;

        int attempts = 0;

        void TryFocusTemporaryTerminal()
        {
            if (!ReferenceEquals(temporaryWorkspace, targetWorkspace) ||
                !temporaryTerminalHost.IsVisible)
            {
                return;
            }

            if (!IsActive || !targetTerminal.IsEffectivelyVisible)
            {
                if (!IsActive && IsVisible)
                    Activate();
            }
            else
            {
                TopLevel.GetTopLevel(this)?.FocusManager?.Focus(
                    targetTerminal,
                    NavigationMethod.Pointer,
                    KeyModifiers.None);
                if (targetTerminal.IsFocused)
                    return;
            }

            if (++attempts < 20)
            {
                DispatcherTimer.RunOnce(
                    TryFocusTemporaryTerminal,
                    TimeSpan.FromMilliseconds(50),
                    DispatcherPriority.Input);
            }
        }

        Dispatcher.UIThread.Post(
            TryFocusTemporaryTerminal,
            DispatcherPriority.Input);
    }

    private void SplitFocusedTerminal(SplitOrientation orientation)
    {
        WorkspaceLayout? workspace = GetActiveLayout();
        if (workspace is null)
            return;

        TerminalView? focusedTerminal = workspace.ActiveTerminal;
        if (focusedTerminal is null)
            return;

        TerminalView newTerminal = CreateTerminal(
            workspace,
            focusedTerminal.WorkingDirectory,
            persistWorkspaceState: !temporaryTerminalHost.IsVisible,
            shellProfile: ShellRegistry.Resolve(focusedTerminal.ShellProfileId));
        if (!workspace.Split(focusedTerminal, newTerminal, orientation))
        {
            newTerminal.Shutdown();
            return;
        }

        workspace.ActiveTerminal = newTerminal;
        newTerminal.Focus();
        _ = CustomCommandService.RunTriggerAsync(
            CustomCommandTriggers.OnPaneOpen,
            newTerminal);
        if (!temporaryTerminalHost.IsVisible)
            SaveWorkspaces();
    }

    private void RunStartupCustomCommands()
    {
        if (customCommandStartupTriggered)
            return;

        customCommandStartupTriggered = true;
        TerminalView? terminal = GetActiveLayout()?.ActiveTerminal;
        _ = CustomCommandService.RunTriggerAsync(
            CustomCommandTriggers.OnStartup,
            terminal);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (customCommandExitCompleted ||
            !CustomCommandService.HasEnabledTrigger(CustomCommandTriggers.OnExit))
            return;

        e.Cancel = true;
        if (customCommandExitRunning)
            return;

        customCommandExitRunning = true;
        _ = RunExitCustomCommands();
    }

    private async Task RunExitCustomCommands()
    {
        try
        {
            await CustomCommandService.RunTriggerAsync(
                CustomCommandTriggers.OnExit,
                GetActiveLayout()?.ActiveTerminal);
        }
        finally
        {
            customCommandExitCompleted = true;
            customCommandExitRunning = false;
            Close();
        }
    }


    private void CloseFocusedPane()
    {
        WorkspaceLayout? workspace = GetActiveLayout();
        if (workspace is null)
            return;

        TerminalView? terminal = workspace.ActiveTerminal;
        if (terminal is null)
            return;

        if (workspace.Terminals.Count == 1){
            Close();
            return;
        }
        
        if (!workspace.ClosePane(terminal))
            return;

        workspace.ActiveTerminal = workspace.GetFirstTerminal();

        if (temporaryTerminalHost.IsVisible)
        {
            workspace.ActiveTerminal?.Focus();
        }
        else
        {
            SaveWorkspaces();
            FocusActivePane();
        }
    }
    
    private void FocusNextPane()
    {
        WorkspaceLayout? workspace = GetActiveLayout();
        if (workspace is null)
            return;

        IReadOnlyList<TerminalView> terminals = workspace.Terminals;
        if (terminals.Count < 2)
            return;

        int currentIndex = -1;
        for (int index = 0; index < terminals.Count; index++)
        {
            if (ReferenceEquals(terminals[index], workspace.ActiveTerminal))
            {
                currentIndex = index;
                break;
            }
        }

        workspace.ActiveTerminal = terminals[(currentIndex + 1) % terminals.Count];
        workspace.ActiveTerminal.Focus();
        if (!temporaryTerminalHost.IsVisible)
            SaveWorkspaces();
    }

    private void FocusPreviusPane()
    {
        WorkspaceLayout? workspace = GetActiveLayout();
        if (workspace is null)
            return;

        IReadOnlyList<TerminalView> terminals = workspace.Terminals;

        if (terminals.Count < 2)
            return;

        int currentIndex = -1;

        for (int index = 0; index < terminals.Count; index++)
        {
            if (ReferenceEquals(terminals[index], workspace.ActiveTerminal))
            {
                currentIndex = index;
                break;
            }
        }

        if (currentIndex == -1)
            return;

        int previousIndex = (currentIndex - 1 + terminals.Count) % terminals.Count;

        workspace.ActiveTerminal = terminals[previousIndex];
        workspace.ActiveTerminal.Focus();
        if (!temporaryTerminalHost.IsVisible)
            SaveWorkspaces();
    }

    private WorkspaceLayout? GetActiveLayout()
    {
        if (temporaryTerminalHost.IsVisible)
            return temporaryWorkspace;

        if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaces.Count)
            return null;

        return workspaces[activeWorkspaceIndex];
    }


    public static void VerticalSplit(bool kam){
        if(kam){
            _instance?.SplitFocusedTerminal(SplitOrientation.Vertical);
        }else{
            _instance?.SplitFocusedTerminal(SplitOrientation.Horizontal);
        }
        return;
    }
    
    public static void CloseSplit(){
        _instance?.CloseFocusedPane();
        return;
    }

    public static void NextFocus(bool zas){
        
        if(zas){
            _instance?.FocusNextPane();
        }else{
            _instance?.FocusPreviusPane();
        }
        return;
    }

    public static void SwitchWorkspace(bool next)
    {
        if (_instance == null || _instance.workspaces.Count == 0)
            return;

        int current = _instance.activeWorkspaceIndex;
        int count = _instance.workspaces.Count;

        int target = next
            ? (current + 1) % count
            : (current - 1 + count) % count;

        _instance.SwitchToWorkspace(target);
    }

    public static void GoToWorkspace(int workspaceNumber)
    {
        if (_instance == null)
            return;

        int index = workspaceNumber - 1;

        if (index < 0 || index >= _instance.workspaceMax)
            return;

        while (_instance.workspaces.Count <= index)
        {
            WorkspaceLayout workspace = _instance.CreateWorkspace();
            _instance.workspaces.Add(workspace);
            _instance.WorkspaceHost.Children.Add(workspace.View);
        }

        _instance.SwitchToWorkspace(index);
    }

    private void ShowWorkspaceLimitMessage(string message)
    {
        WorkspaceInfoText.Text = message;
        WorkspaceInfoBox.IsVisible = true;
        workspaceInfoTimer.Stop();
        workspaceInfoTimer.Start();
    }

    private static int? GetWorkspaceDigit(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
            return (int)key - (int)Key.D0;

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return (int)key - (int)Key.NumPad0;

        return null;
    }

    public static void ShowDuckyManager()
    {
        if (_instance == null)
            return;

        if (_instance.managerWindow is { IsVisible: true })
        {
            _instance.managerWindow.Close();
            return;
        }

        DuckyManager manager = new();
        _instance.managerWindow = manager;
        manager.AddHandler(
            KeyDownEvent,
            _instance._keyListener.OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        manager.AddHandler(
            KeyUpEvent,
            _instance._keyListener.OnKeyUp,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        manager.Closed += (_, _) =>
        {
            if (ReferenceEquals(_instance?.managerWindow, manager))
                _instance.managerWindow = null;
        };
        _ = manager.ShowDialog(_instance);
    }

    public static void CloseDuckyManager()
    {
        _instance?.managerWindow?.Close();
    }

    public static void OpenTimerWindow()
    {
        if (_instance == null)
            return;

        if (_instance.timerWindow == null)
        {
            _instance.timerWindow = new TimerWindow( _instance.timerService, _instance.currentConfig.DefaultCountdownTime);

            _instance.timerWindow.AddHandler(
                KeyDownEvent,
                _instance._keyListener.OnKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

            _instance.timerWindow.AddHandler(
                KeyUpEvent,
                _instance._keyListener.OnKeyUp,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

            _instance.timerWindow.Closed += (_, _) =>
            {
                _instance.timerWindow = null;
            };
        }

        if (!_instance.timerWindow.IsVisible)
            _instance.timerWindow.Show(_instance);

        _instance.timerWindow.Activate();
    }

    public static void ChangeFontSize(bool increase)
    {
        if (_instance == null)
            return;

        double newFontSize = _instance.currentConfig.FontSize + (increase ? 1 : -1);
        _instance.currentConfig.FontSize = Math.Clamp(newFontSize, 6, 72);

        foreach (WorkspaceLayout workspace in _instance.workspaces)
        {
            foreach (TerminalView terminal in workspace.Terminals)
                terminal.ChangeFontSize(increase);
        }

        if (_instance.temporaryWorkspace is not null)
        {
            foreach (TerminalView terminal in _instance.temporaryWorkspace.Terminals)
                terminal.ChangeFontSize(increase);
        }
    }

}
