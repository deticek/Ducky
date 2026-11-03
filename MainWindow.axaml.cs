using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace ducky;

public partial class MainWindow : Window
{
    private readonly List<WorkspaceLayout> workspaces = new();
    private readonly Grid temporaryTerminalHost = new() { IsVisible = false };
    private readonly ConfigHotReloader configHotReloader;
    private WorkspaceLayout? temporaryWorkspace;
    private Config currentConfig = ConfigReder.GetConfig();
    private int workspaceMax;
    private readonly DispatcherTimer workspaceInfoTimer;
    private int activeWorkspaceIndex = -1;
    private bool isClosing;
    private KeyListener _keyListener = null!;
    private static MainWindow? _instance;
    
    //timer
    private readonly TimerService timerService = new();
    private TimerWindow? timerWindow;
    private readonly DispatcherTimer clockTimer = new();
    private bool timerFinished = false;

    private bool skipWorkspaceSave = false;

    //commands
    public static CommandManager Commands { get; } = new CommandManager();

    public MainWindow()
    {
        InitializeComponent();

        Commands.Register(new TestCommand());
        Commands.Register(new TimerStartCommand());
        Commands.Register(new TimerCountdownCommand());
        Commands.Register(new TimerPauseCommand());
        Commands.Register(new TimerResetCommand());
        Commands.Register(new TimerCloseCommand());
        Commands.Register(new WorkspaceResetCommand());

        WorkspaceHost.Children.Add(temporaryTerminalHost);
        Navbar.IsVisible = currentConfig.ShowNavbar;

        HelpButton.Click += (_, _) =>
        {
            HelpWindow.Show(this, ConfigReder.GetConfig());
        };

        ManagerButton.Click += (_, _) =>
        {
            DuckyManager manager = new DuckyManager();
            manager.Show(this);
        };

        _instance = this;

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

        Closed += (_, _) =>
        {
            SaveWorkspaces();
            isClosing = true;

            foreach (WorkspaceLayout workspace in workspaces)
                workspace.Shutdown();

            temporaryWorkspace?.Shutdown();
            configHotReloader.Dispose();
            workspaceInfoTimer.Stop();
        };
        Opened += (_, _) => FocusVisibleTerminal();
        Activated += (_, _) => FocusVisibleTerminal();

        WorkspaceSessionState? savedState = LoadWorkspaceState();
        if (savedState is { Workspaces.Count: > 0 })
        {
            var partiallyRestoredTerminals = new List<TerminalView>();
            try
            {
                foreach (WorkspaceLayoutState workspaceState in
                         savedState.Workspaces.Take(workspaceMax))
                {
                    var workspace = new WorkspaceLayout();
                    workspace.Restore(
                        workspaceState,
                        path =>
                        {
                            TerminalView terminal = CreateTerminal(workspace, path);
                            partiallyRestoredTerminals.Add(terminal);
                            return terminal;
                        });
                    workspaces.Add(workspace);
                    WorkspaceHost.Children.Add(workspace.View);
                    partiallyRestoredTerminals.Clear();
                }
            }
            catch (Exception ex)
            {
                DuckyLog.Log("Could not restore workspaces: " + ex);
                foreach (TerminalView terminal in partiallyRestoredTerminals)
                    terminal.Shutdown();

                foreach (WorkspaceLayout workspace in workspaces)
                    workspace.Shutdown();
                workspaces.Clear();
                WorkspaceHost.Children.Clear();
            }
        }

        if (workspaces.Count == 0)
        {
            for (int index = 0; index < workspaceMax; index++)
            {
                WorkspaceLayout workspace = CreateWorkspace();
                workspaces.Add(workspace);
                WorkspaceHost.Children.Add(workspace.View);
            }
        }

        while (workspaces.Count < workspaceMax)
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

        if (currentConfig.AlwaysOpenInTemporaryTerminal)
            OpenTemporaryTerminalInstance();
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
        var workspace = new WorkspaceLayout();
        TerminalView terminal = CreateTerminal(workspace, null);
        workspace.Initialize(terminal);
        return workspace;
    }

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
        bool persistWorkspaceState = true)
    {
        var terminal = new TerminalView(workingDirectory);
        
        terminal.GotFocus += (_, _) =>
        {
            workspace.ActiveTerminal = terminal;
            if (persistWorkspaceState)
                SaveWorkspaces();
        };
        if (persistWorkspaceState)
            terminal.WorkingDirectoryChanged += _ => SaveWorkspaces();
        terminal.Preload();
        return terminal;
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
            WorkspaceStore.Save(new WorkspaceSessionState
            {
                ActiveWorkspaceIndex = activeWorkspaceIndex,
                Workspaces = workspaces.Select(workspace => workspace.Save()).ToList()
            });
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

    public void ResetWorkspaces()
    {
        try
        {
            string homeDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

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

            for (int index = 0; index < workspaceMax; index++)
            {
                WorkspaceLayout workspace = CreateWorkspace();
                TerminalView terminal = CreateTerminal(workspace, homeDirectory);

                workspace.Initialize(terminal);
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

        activeWorkspaceIndex = index;
        temporaryTerminalHost.IsVisible = false;
        for (int workspaceIndex = 0; workspaceIndex < workspaces.Count; workspaceIndex++)
            workspaces[workspaceIndex].View.IsVisible = workspaceIndex == index;

        Title = $"Ducky - Workspace {index + 1}";
        ActiveWorkspaceLabel.Text = $"Workspace {index + 1}";
        SaveWorkspaces();
        FocusActivePane();
    }

    private void OpenTemporaryTerminalInstance()
    {
        temporaryWorkspace?.Shutdown();
        temporaryTerminalHost.Children.Clear();

        var workspace = new WorkspaceLayout();
        TerminalView terminal = CreateTerminal(workspace, null, persistWorkspaceState: false);
        terminal.AttachedToVisualTree += (_, _) => FocusTemporaryTerminal();
        workspace.Initialize(terminal);
        temporaryWorkspace = workspace;
        temporaryTerminalHost.Children.Add(workspace.View);

        foreach (WorkspaceLayout loadedWorkspace in workspaces)
            loadedWorkspace.View.IsVisible = false;

        temporaryTerminalHost.IsVisible = true;
        Title = "Ducky - Temporary";
        ActiveWorkspaceLabel.Text = "Temporary";
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
            persistWorkspaceState: !temporaryTerminalHost.IsVisible);
        if (!workspace.Split(focusedTerminal, newTerminal, orientation))
        {
            newTerminal.Shutdown();
            return;
        }

        workspace.ActiveTerminal = newTerminal;
        newTerminal.Focus();
        if (!temporaryTerminalHost.IsVisible)
            SaveWorkspaces();
    }

    private void CloseFocusedPane()
    {
        if (temporaryTerminalHost.IsVisible)
        {
            Close();
            return;
        }

        WorkspaceLayout? workspace = GetActiveLayout();
        if (workspace is null)
            return;

        TerminalView? terminal = workspace.ActiveTerminal;
        if (terminal is null)
            return;

        bool isLastPane = workspace.Terminals.Count == 1;
        if (isLastPane)
        {
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

    public static void ShowShortcutsHelp()
    {
        if (_instance == null)
            return;

        HelpWindow.Show(_instance, ConfigReder.GetConfig());
    }

    public static void ShowDuckyManager()
    {
        if (_instance == null)
            return;

        DuckyManager manager = new DuckyManager();
            manager.Show(_instance);
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
                terminal.ApplyConfig(_instance.currentConfig);
        }

        if (_instance.temporaryWorkspace is not null)
        {
            foreach (TerminalView terminal in _instance.temporaryWorkspace.Terminals)
                terminal.ApplyConfig(_instance.currentConfig);
        }
    }

}
