using System;
using System.Collections.Generic;
using System.Linq;

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
    private TerminalView? temporaryTerminal;
    private Config currentConfig = ConfigReder.GetConfig();
    private int workspaceMax;
    private readonly DispatcherTimer workspaceInfoTimer;
    private int activeWorkspaceIndex = -1;
    private bool isClosing;
    private KeyListener _keyListener = null!;
    private static MainWindow? _instance;

    private bool skipWorkspaceSave = false;

    public MainWindow()
    {
        InitializeComponent();

        WorkspaceHost.Children.Add(temporaryTerminalHost);
        Navbar.IsVisible = currentConfig.ShowNavbar;

        HelpButton.Click += (_, _) =>
        {
            HelpWindow.Show(this, ConfigReder.GetConfig());
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

        _keyListener = new KeyListener(() =>
        {
            if (temporaryTerminalHost.IsVisible)
                return temporaryTerminal;

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

            temporaryTerminal?.Shutdown();
            configHotReloader.Dispose();
            workspaceInfoTimer.Stop();
        };
        Opened += (_, _) => FocusActivePane();

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
            workspaceMax = updatedConfig.WorkspaceMax;
            Navbar.IsVisible = updatedConfig.ShowNavbar;
            _keyListener.UpdateConfig(updatedConfig);

            foreach (WorkspaceLayout workspace in workspaces)
            {
                foreach (TerminalView terminal in workspace.Terminals)
                    terminal.ApplyConfig(updatedConfig);
            }
            temporaryTerminal?.ApplyConfig(updatedConfig);

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
        string? workingDirectory)
    {
        var terminal = new TerminalView(workingDirectory);
        
        terminal.GotFocus += (_, _) =>
        {
            workspace.ActiveTerminal = terminal;
            SaveWorkspaces();
        };
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
            skipWorkspaceSave = true;
            isClosing = true;

            foreach (WorkspaceLayout workspace in workspaces)
            {
                workspace.Shutdown();
            }

            temporaryTerminal?.Shutdown();

            string path = DuckyPaths.WorkspaceStatePath;

            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }

            configHotReloader.Dispose();
            workspaceInfoTimer.Stop();

            Close();
        }
        catch (Exception ex)
        {
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
        temporaryTerminal?.Shutdown();
        temporaryTerminalHost.Children.Clear();

        var terminal = new TerminalView();
        terminal.ApplyConfig(currentConfig);
        terminal.Preload();
        temporaryTerminal = terminal;
        temporaryTerminalHost.Children.Add(terminal);

        foreach (WorkspaceLayout workspace in workspaces)
            workspace.View.IsVisible = false;

        temporaryTerminalHost.IsVisible = true;
        Title = "Ducky - Temporary";
        ActiveWorkspaceLabel.Text = "Temporary";
        Dispatcher.UIThread.Post(() => terminal.Focus());
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
            if (index != activeWorkspaceIndex ||
                index < 0 ||
                index >= workspaces.Count)
            {
                return;
            }

            WorkspaceLayout workspace = workspaces[index];
            if (!workspace.Contains(workspace.ActiveTerminal))
                workspace.ActiveTerminal = workspace.GetFirstTerminal();

            workspace.ActiveTerminal?.Focus();
        });
    }

    private void SplitFocusedTerminal(SplitOrientation orientation)
    {
        if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaces.Count)
            return;

        WorkspaceLayout workspace = workspaces[activeWorkspaceIndex];
        TerminalView? focusedTerminal = workspace.ActiveTerminal;
        if (focusedTerminal is null)
            return;

        TerminalView newTerminal = CreateTerminal(
            workspace,
            focusedTerminal.WorkingDirectory);
        if (!workspace.Split(focusedTerminal, newTerminal, orientation))
        {
            newTerminal.Shutdown();
            return;
        }

        workspace.ActiveTerminal = newTerminal;
        newTerminal.Focus();
        SaveWorkspaces();
    }

    private void CloseFocusedPane()
    {
        if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaces.Count)
            return;

        WorkspaceLayout workspace = workspaces[activeWorkspaceIndex];
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
        SaveWorkspaces();
        FocusActivePane();
    }
    
    private void FocusNextPane()
    {
        if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaces.Count)
            return;

        WorkspaceLayout workspace = workspaces[activeWorkspaceIndex];
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
        SaveWorkspaces();
    }

    private void FocusPreviusPane()
    {
        if (activeWorkspaceIndex < 0 || activeWorkspaceIndex >= workspaces.Count)
            return;

        WorkspaceLayout workspace = workspaces[activeWorkspaceIndex];
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
        SaveWorkspaces();
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

        _instance.temporaryTerminal?.ApplyConfig(_instance.currentConfig);
    }

}
