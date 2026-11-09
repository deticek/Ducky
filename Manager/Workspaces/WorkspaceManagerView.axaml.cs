
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace ducky;

public partial class WorkspaceManagerView : UserControl
{
    private WorkspaceSessionState state = new();

    private string? shortcutTargetId;
    private bool shortcutIsTemporary;

    private HashSet<string> expandedWorkspaces = new();

    private string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private string DefaultShell =>
        ShellRegistry.Resolve(ConfigReder.GetConfig().Shell)?.Id ??
        (OperatingSystem.IsWindows() ? "powershell.exe" : DuckyPaths.GetLinuxDefaultShell());

    public WorkspaceManagerView()
    {
        InitializeComponent();

        LoadState();

        MaxWorkspacesInput.Text = state.MaxWorkspaces.ToString();

        RefreshAll();
    }

    // ---------------------------------------------------------
    // STATE
    // ---------------------------------------------------------

    private void LoadState()
    {
        try
        {
            state = WorkspaceStore.Load() ?? CreateDefaultState();

            if (state.Workspaces.Count == 0)
                state.Workspaces.Add(CreateWorkspace(1));

            if (state.TemporaryWorkspace is null)
                state.TemporaryWorkspace = CreateTemporaryWorkspace();

            if (state.TemporaryWorkspace.Root is null)
            {
                state.TemporaryWorkspace.Root =
                    CreateLeaf(state.TemporaryWorkspace.WorkingDirectory);
            }
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Workspace manager load error: " + ex);

            state = CreateDefaultState();

            StatusText.Text =
                "Could not load workspace data. Default data was loaded.";
        }
    }

    private WorkspaceSessionState CreateDefaultState()
    {
        var result = new WorkspaceSessionState
        {
            MaxWorkspaces = 20,
            ActiveWorkspaceIndex = 0
        };

        result.Workspaces.Add(CreateWorkspace(1));
        result.TemporaryWorkspace = CreateTemporaryWorkspace();

        return result;
    }

    private WorkspaceData CreateWorkspace(int number)
    {
        return new WorkspaceData
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Workspace " + number,
            DefaultShell = DefaultShell,
            Root = CreateLeaf(HomeDirectory),
            ActivePaneIndex = 0
        };
    }

    private TemporaryWorkspaceState CreateTemporaryWorkspace()
    {
        return new TemporaryWorkspaceState
        {
            WorkingDirectory = HomeDirectory,
            DefaultShell = DefaultShell,
            Root = CreateLeaf(HomeDirectory),
            ActivePaneIndex = 0
        };
    }

    private WorkspacePaneState CreateLeaf(string? path)
    {
        return new WorkspacePaneState
        {
            WorkingDirectory = path,
            Shell = null
        };
    }

    private WorkspacePaneState ClonePane(WorkspacePaneState pane)
    {
        string json = JsonSerializer.Serialize(pane);

        return JsonSerializer.Deserialize<WorkspacePaneState>(json)
            ?? CreateLeaf(HomeDirectory);
    }

    private bool SaveState()
    {
        try
        {
            WorkspaceStore.Save(state);
            MainWindow.ApplyWorkspaceState(state);
            StatusText.Text = "Changes saved.";
            UpdateCount();
            return true;
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Workspace save error: " + ex);
            StatusText.Text = "Save failed: " + ex.Message;
            return false;
        }
    }

    private void RefreshAll()
    {
        RefreshWorkspaceCards();
        RefreshTemporaryEditor();
        UpdateCount();
    }

    private void UpdateCount()
    {
        WorkspaceCountText.Text =
            state.Workspaces.Count + " / " + state.MaxWorkspaces + " workspaces";
    }

    // ---------------------------------------------------------
    // WORKSPACE CARDS
    // ---------------------------------------------------------

    private void RefreshWorkspaceCards()
    {
        WorkspacesPanel.Children.Clear();

        string search = SearchInput.Text?.Trim() ?? "";

        for (int i = 0; i < state.Workspaces.Count; i++)
        {
            WorkspaceData workspace = state.Workspaces[i];

            if (!workspace.Title.Contains(
                search,
                StringComparison.OrdinalIgnoreCase))
            {
                if (workspace.Root is null ||
                    !ContainsPath(workspace.Root, search))
                {
                    continue;
                }
            }

            WorkspacesPanel.Children.Add(
                BuildWorkspaceCard(workspace, i));
        }
    }

    private bool ContainsPath(WorkspacePaneState pane, string search)
    {
        if ((pane.WorkingDirectory ?? "").Contains(
            search,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pane.First is not null && ContainsPath(pane.First, search))
            return true;

        if (pane.Second is not null && ContainsPath(pane.Second, search))
            return true;

        return false;
    }

    private Control BuildWorkspaceCard(
        WorkspaceData workspace,
        int index)
    {
        var expander = new Expander
        {
            IsExpanded = expandedWorkspaces.Contains(workspace.Id),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        expander.Expanded += (_, _) =>
        {
            expandedWorkspaces.Add(workspace.Id);
        };

        expander.Collapsed += (_, _) =>
        {
            expandedWorkspaces.Remove(workspace.Id);
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(4)
        };

        var title = new TextBlock
        {
            Text = workspace.Title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var number = new TextBlock
        {
            Text = "#" + (index + 1),
            Foreground = Brushes.Gray,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        Grid.SetColumn(number, 1);

        header.Children.Add(title);
        header.Children.Add(number);

        expander.Header = header;

        var content = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(12, 10)
        };

        // TITLE
        content.Children.Add(CreateLabel("Title"));

        var titleInput = new TextBox
        {
            Text = workspace.Title,
            Watermark = "Workspace title"
        };

        titleInput.TextChanged += (_, _) =>
        {
            workspace.Title = titleInput.Text ?? "";
            title.Text = workspace.Title;
            SaveState();
        };

        content.Children.Add(titleInput);

        // DEFAULT SHELL
        content.Children.Add(CreateLabel("Default shell"));

        ComboBox defaultShell = CreateShellCombo(workspace.DefaultShell);

        defaultShell.SelectionChanged += (_, _) =>
        {
            if (defaultShell.SelectedItem is ShellProfile profile)
                workspace.DefaultShell = profile.Id;

            SaveState();
        };

        content.Children.Add(defaultShell);

        content.Children.Add(CreateLabel("Terminal theme"));

        ComboBox themeCombo = new()
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        themeCombo.Items.Add(new ComboBoxItem
        {
            Content = "Use default theme",
            Tag = ""
        });

        List<ThemeData> availableThemes = ThemeService.LoadAll();
        foreach (ThemeData theme in availableThemes)
        {
            themeCombo.Items.Add(new ComboBoxItem
            {
                Content = theme.Name,
                Tag = theme.Id
            });
        }

        int selectedThemeIndex = 0;
        if (!string.IsNullOrWhiteSpace(workspace.ThemeId))
        {
            for (int itemIndex = 1; itemIndex < themeCombo.Items.Count; itemIndex++)
            {
                if (themeCombo.Items[itemIndex] is ComboBoxItem item &&
                    item.Tag?.ToString() == workspace.ThemeId)
                {
                    selectedThemeIndex = itemIndex;
                    break;
                }
            }
        }
        themeCombo.SelectedIndex = selectedThemeIndex;
        themeCombo.SelectionChanged += (_, _) =>
        {
            string? themeId = (themeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            workspace.ThemeId = string.IsNullOrWhiteSpace(themeId) ? null : themeId;
            if (SaveState())
                MainWindow.ApplyWorkspaceThemes(state);
        };
        content.Children.Add(themeCombo);

        // SPLITS
        content.Children.Add(CreateLabel("Terminal splits"));

        if (workspace.Root is null)
            workspace.Root = CreateLeaf(HomeDirectory);

        content.Children.Add(
            BuildPaneEditor(
                workspace.Root,
                pane => workspace.Root = pane,
                workspace));

        // SHORTCUT
        content.Children.Add(CreateDivider());

        content.Children.Add(CreateLabel("Keyboard shortcut"));

        var shortcutRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        var shortcutText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(workspace.KeyShortcut)
                ? "Not configured"
                : workspace.KeyShortcut,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var shortcutButton = new Button
        {
            Content = "Change"
        };

        shortcutButton.Click += (_, _) =>
        {
            StartShortcutCapture(workspace.Id, false);
        };

        shortcutRow.Children.Add(shortcutText);
        Grid.SetColumn(shortcutButton, 1);
        shortcutRow.Children.Add(shortcutButton);

        content.Children.Add(shortcutRow);

        // WORKSPACE ACTIONS
        content.Children.Add(CreateDivider());

        var actions = new WrapPanel
        {
            ItemSpacing = 6,
            LineSpacing = 6
        };

        var upButton = CreateButton("Move up", () =>
        {
            MoveWorkspace(workspace, -1);
        });

        var downButton = CreateButton("Move down", () =>
        {
            MoveWorkspace(workspace, 1);
        });

        var duplicateButton = CreateButton("Duplicate", () =>
        {
            DuplicateWorkspace(workspace);
        });

        var deleteButton = CreateButton("Delete", () =>
        {
            DeleteWorkspace(workspace);
        });

        actions.Children.Add(upButton);
        actions.Children.Add(downButton);
        actions.Children.Add(duplicateButton);
        actions.Children.Add(deleteButton);

        content.Children.Add(actions);

        expander.Content = content;

        return expander;
    }

    // ---------------------------------------------------------
    // PANE EDITOR
    // ---------------------------------------------------------

    private Control BuildPaneEditor(
        WorkspacePaneState pane,
        Action<WorkspacePaneState> replace,
        WorkspaceData? workspace)
    {
        var border = CreatePaneBorder();

        var panel = new StackPanel
        {
            Spacing = 8
        };

        if (pane.Orientation is not null)
        {
            panel.Children.Add(CreateLabel(
                "Split - " + pane.Orientation));

            var removeButton = CreateButton("Remove split", () =>
            {
                if (pane.First is null)
                    return;

                replace(ClonePane(pane.First));
                SaveState();
                RefreshWorkspaceCards();
            });

            panel.Children.Add(removeButton);

            if (pane.First is not null)
            {
                panel.Children.Add(BuildPaneEditor(
                    pane.First,
                    updated => pane.First = updated,
                    workspace));
            }

            if (pane.Second is not null)
            {
                panel.Children.Add(BuildPaneEditor(
                    pane.Second,
                    updated => pane.Second = updated,
                    workspace));
            }
        }
        else
        {
            panel.Children.Add(CreateLabel("Working directory"));

            var pathInput = new TextBox
            {
                Text = pane.WorkingDirectory ?? "",
                Watermark = "Working directory"
            };

            pathInput.LostFocus += (_, _) =>
            {
                pane.WorkingDirectory = pathInput.Text;
                SaveState();
            };

            var pathRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 6
            };

            pathRow.Children.Add(pathInput);

            var browseButton = CreateButton("Browse", async () =>
            {
                string? path = await BrowseDirectoryAsync();

                if (path is null)
                    return;

                pathInput.Text = path;
                pane.WorkingDirectory = path;

                SaveState();
            });

            Grid.SetColumn(browseButton, 1);
            pathRow.Children.Add(browseButton);

            panel.Children.Add(pathRow);

            panel.Children.Add(CreateLabel("Shell"));

            ComboBox shellCombo = CreateShellCombo(
                pane.Shell ?? workspace?.DefaultShell ?? DefaultShell);

            shellCombo.SelectionChanged += (_, _) =>
            {
                string? selected =
                    (shellCombo.SelectedItem as ShellProfile)?.Id;

                if (workspace is not null &&
                    selected == ShellRegistry.Resolve(workspace.DefaultShell)?.Id)
                {
                    pane.Shell = null;
                }
                else
                {
                    pane.Shell = selected;
                }

                SaveState();
            };

            panel.Children.Add(shellCombo);

            var splitButtons = new WrapPanel
            {
                ItemSpacing = 6,
                LineSpacing = 6
            };

            splitButtons.Children.Add(CreateButton(
                "Split vertical",
                () => AddSplit(pane, "Vertical", replace)));

            splitButtons.Children.Add(CreateButton(
                "Split horizontal",
                () => AddSplit(pane, "Horizontal", replace)));

            panel.Children.Add(splitButtons);
        }

        border.Child = panel;

        return border;
    }

    private void AddSplit(
        WorkspacePaneState pane,
        string orientation,
        Action<WorkspacePaneState> replace)
    {
        WorkspacePaneState first = ClonePane(pane);
        WorkspacePaneState second = ClonePane(pane);

        pane.WorkingDirectory = null;
        pane.Shell = null;
        pane.Orientation = orientation;
        pane.First = first;
        pane.Second = second;

        replace(pane);

        SaveState();
        RefreshWorkspaceCards();
    }

    // ---------------------------------------------------------
    // TEMPORARY WORKSPACE
    // ---------------------------------------------------------

    private void RefreshTemporaryEditor()
    {
        TemporaryEditorPanel.Children.Clear();

        TemporaryWorkspaceState temporary = state.TemporaryWorkspace;

        TemporaryEditorPanel.Children.Add(CreateLabel("Working directory"));

        var pathInput = new TextBox
        {
            Text = temporary.WorkingDirectory,
            Watermark = "Working directory"
        };

        pathInput.LostFocus += (_, _) =>
        {
            temporary.WorkingDirectory = pathInput.Text ?? "";

            if (temporary.Root is { Orientation: null } root)
                root.WorkingDirectory = temporary.WorkingDirectory;

            SaveState();
        };

        var pathRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6
        };

        pathRow.Children.Add(pathInput);

        var browseButton = CreateButton("Browse", async () =>
        {
            string? path = await BrowseDirectoryAsync();

            if (path is null)
                return;

            pathInput.Text = path;
            temporary.WorkingDirectory = path;

            if (temporary.Root is { Orientation: null } root)
                root.WorkingDirectory = path;

            SaveState();
        });

        Grid.SetColumn(browseButton, 1);
        pathRow.Children.Add(browseButton);

        TemporaryEditorPanel.Children.Add(pathRow);

        TemporaryEditorPanel.Children.Add(CreateLabel("Default shell"));

        ComboBox shellCombo = CreateShellCombo(temporary.DefaultShell);

        shellCombo.SelectionChanged += (_, _) =>
        {
            if (shellCombo.SelectedItem is ShellProfile profile)
                temporary.DefaultShell = profile.Id;

            SaveState();
        };

        TemporaryEditorPanel.Children.Add(shellCombo);

        TemporaryEditorPanel.Children.Add(CreateLabel("Terminal theme"));

        ComboBox temporaryThemeCombo = new()
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        temporaryThemeCombo.Items.Add(new ComboBoxItem
        {
            Content = "Use default theme",
            Tag = ""
        });

        List<ThemeData> temporaryThemes = ThemeService.LoadAll();
        foreach (ThemeData theme in temporaryThemes)
        {
            temporaryThemeCombo.Items.Add(new ComboBoxItem
            {
                Content = theme.Name,
                Tag = theme.Id
            });
        }

        int temporaryThemeIndex = 0;
        if (!string.IsNullOrWhiteSpace(temporary.ThemeId))
        {
            for (int itemIndex = 1; itemIndex < temporaryThemeCombo.Items.Count; itemIndex++)
            {
                if (temporaryThemeCombo.Items[itemIndex] is ComboBoxItem item &&
                    item.Tag?.ToString() == temporary.ThemeId)
                {
                    temporaryThemeIndex = itemIndex;
                    break;
                }
            }
        }

        temporaryThemeCombo.SelectedIndex = temporaryThemeIndex;
        temporaryThemeCombo.SelectionChanged += (_, _) =>
        {
            string? themeId =
                (temporaryThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            temporary.ThemeId = string.IsNullOrWhiteSpace(themeId) ? null : themeId;
            if (SaveState())
                MainWindow.ApplyWorkspaceThemes(state);
        };
        TemporaryEditorPanel.Children.Add(temporaryThemeCombo);

        TemporaryEditorPanel.Children.Add(CreateLabel("Terminal splits"));

        if (temporary.Root is null)
            temporary.Root = CreateLeaf(temporary.WorkingDirectory);

        TemporaryEditorPanel.Children.Add(
            BuildTemporaryPaneEditor(
                temporary.Root,
                pane => temporary.Root = pane));

        TemporaryEditorPanel.Children.Add(CreateDivider());

        TemporaryEditorPanel.Children.Add(CreateLabel("Keyboard shortcut"));

        var shortcutRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        var shortcutText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(temporary.KeyShortcut)
                ? "Not configured"
                : temporary.KeyShortcut,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var shortcutButton = CreateButton("Change", () =>
        {
            StartShortcutCapture("", true);
        });

        shortcutRow.Children.Add(shortcutText);
        Grid.SetColumn(shortcutButton, 1);
        shortcutRow.Children.Add(shortcutButton);

        TemporaryEditorPanel.Children.Add(shortcutRow);
    }

    private Control BuildTemporaryPaneEditor(
        WorkspacePaneState pane,
        Action<WorkspacePaneState> replace)
    {
        var border = CreatePaneBorder();

        var panel = new StackPanel
        {
            Spacing = 8
        };

        if (pane.Orientation is not null)
        {
            panel.Children.Add(CreateLabel(
                "Split - " + pane.Orientation));

            panel.Children.Add(CreateButton("Remove split", () =>
            {
                if (pane.First is null)
                    return;

                replace(ClonePane(pane.First));
                SaveState();
                RefreshTemporaryEditor();
            }));

            if (pane.First is not null)
            {
                panel.Children.Add(BuildTemporaryPaneEditor(
                    pane.First,
                    updated => pane.First = updated));
            }

            if (pane.Second is not null)
            {
                panel.Children.Add(BuildTemporaryPaneEditor(
                    pane.Second,
                    updated => pane.Second = updated));
            }
        }
        else
        {
            panel.Children.Add(CreateLabel("Working directory"));

            var pathInput = new TextBox
            {
                Text = pane.WorkingDirectory ?? "",
                Watermark = "Working directory"
            };

            pathInput.LostFocus += (_, _) =>
            {
                pane.WorkingDirectory = pathInput.Text;
                SaveState();
            };

            panel.Children.Add(pathInput);

            panel.Children.Add(CreateLabel("Shell"));

            ComboBox shellCombo = CreateShellCombo(
                pane.Shell ?? state.TemporaryWorkspace.DefaultShell);

            shellCombo.SelectionChanged += (_, _) =>
            {
                string? selected =
                    (shellCombo.SelectedItem as ShellProfile)?.Id;

                if (selected ==
                    ShellRegistry.Resolve(state.TemporaryWorkspace.DefaultShell)?.Id)
                    pane.Shell = null;
                else
                    pane.Shell = selected;

                SaveState();
            };

            panel.Children.Add(shellCombo);

            var splitButtons = new WrapPanel
            {
                ItemSpacing = 6,
                LineSpacing = 6
            };

            splitButtons.Children.Add(CreateButton(
                "Split vertical",
                () => AddTemporarySplit(pane, "Vertical", replace)));

            splitButtons.Children.Add(CreateButton(
                "Split horizontal",
                () => AddTemporarySplit(pane, "Horizontal", replace)));

            panel.Children.Add(splitButtons);
        }

        border.Child = panel;

        return border;
    }

    private void AddTemporarySplit(
        WorkspacePaneState pane,
        string orientation,
        Action<WorkspacePaneState> replace)
    {
        WorkspacePaneState first = ClonePane(pane);
        WorkspacePaneState second = ClonePane(pane);

        pane.WorkingDirectory = null;
        pane.Shell = null;
        pane.Orientation = orientation;
        pane.First = first;
        pane.Second = second;

        replace(pane);

        SaveState();
        RefreshTemporaryEditor();
    }

    // ---------------------------------------------------------
    // SHORTCUT CAPTURE
    // ---------------------------------------------------------

    private void StartShortcutCapture(
        string workspaceId,
        bool temporary)
    {
        shortcutTargetId = workspaceId;
        shortcutIsTemporary = temporary;

        ShortcutPreview.Text = "Waiting for input...";
        ShortcutOverlay.IsVisible = true;

        ShortcutOverlay.Focus();
    }

    private void ShortcutOverlay_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            ShortcutOverlay.IsVisible = false;
            return;
        }

        if (e.Key == Key.Back)
        {
            SaveShortcut("");
            return;
        }

        if (IsModifierKey(e.Key))
            return;

        string shortcut = BuildShortcut(e);

        if (string.IsNullOrWhiteSpace(shortcut))
            return;

        ShortcutPreview.Text = shortcut;

        SaveShortcut(shortcut);
    }

    private bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl ||
               key == Key.RightCtrl ||
               key == Key.LeftAlt ||
               key == Key.RightAlt ||
               key == Key.LeftShift ||
               key == Key.RightShift ||
               key == Key.LWin ||
               key == Key.RWin;
    }

    private string BuildShortcut(KeyEventArgs e)
    {
        List<string> parts = new();

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Meta");

        parts.Add(e.Key.ToString());

        return string.Join("+", parts);
    }

    private void SaveShortcut(string shortcut)
    {
        if (shortcutIsTemporary)
        {
            state.TemporaryWorkspace.KeyShortcut = shortcut;
        }
        else
        {
            WorkspaceData? workspace = state.Workspaces.FirstOrDefault(
                item => item.Id == shortcutTargetId);

            if (workspace is not null)
                workspace.KeyShortcut = shortcut;
        }

        ShortcutOverlay.IsVisible = false;
        SaveState();
        if (shortcutIsTemporary)
            RefreshTemporaryEditor();
        else
            RefreshWorkspaceCards();
    }

    // ---------------------------------------------------------
    // WORKSPACE ACTIONS
    // ---------------------------------------------------------

    private void AddWorkspace_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (state.Workspaces.Count >= state.MaxWorkspaces)
        {
            StatusText.Text = "Workspace limit reached.";
            return;
        }

        WorkspaceData workspace =
            CreateWorkspace(state.Workspaces.Count + 1);

        state.Workspaces.Add(workspace);

        SaveState();
        RefreshWorkspaceCards();
    }

    private void DuplicateWorkspace(WorkspaceData workspace)
    {
        if (state.Workspaces.Count >= state.MaxWorkspaces)
        {
            StatusText.Text = "Workspace limit reached.";
            return;
        }

        string json = JsonSerializer.Serialize(workspace);

        WorkspaceData? duplicate =
            JsonSerializer.Deserialize<WorkspaceData>(json);

        if (duplicate is null)
            return;

        duplicate.Id = Guid.NewGuid().ToString();
        duplicate.Title = workspace.Title + " Copy";
        duplicate.KeyShortcut = "";

        int index = state.Workspaces.IndexOf(workspace) + 1;

        state.Workspaces.Insert(index, duplicate);

        SaveState();
        RefreshWorkspaceCards();
    }

    private void DeleteWorkspace(WorkspaceData workspace)
    {
        state.Workspaces.Remove(workspace);

        if (state.Workspaces.Count == 0)
            state.Workspaces.Add(CreateWorkspace(1));

        state.ActiveWorkspaceIndex = Math.Clamp(
            state.ActiveWorkspaceIndex,
            0,
            state.Workspaces.Count - 1);

        SaveState();
        RefreshWorkspaceCards();
    }

    private void MoveWorkspace(
        WorkspaceData workspace,
        int direction)
    {
        int index = state.Workspaces.IndexOf(workspace);
        int newIndex = index + direction;

        if (newIndex < 0 || newIndex >= state.Workspaces.Count)
            return;

        state.Workspaces.RemoveAt(index);
        state.Workspaces.Insert(newIndex, workspace);

        SaveState();
        RefreshWorkspaceCards();
    }

    // ---------------------------------------------------------
    // GLOBAL SETTINGS
    // ---------------------------------------------------------

    private void MaxWorkspacesInput_LostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        SaveMaximumValue();
    }

    private void SaveMaximumValue()
    {
        if (!int.TryParse(MaxWorkspacesInput.Text, out int maximum))
        {
            StatusText.Text = "Enter a whole number from 1 to 100.";
            return;
        }

        if (maximum < 1 || maximum > 100)
        {
            StatusText.Text = "Maximum workspaces must be between 1 and 100.";
            return;
        }

        state.MaxWorkspaces = maximum;
        if (state.Workspaces.Count > maximum)
            state.Workspaces.RemoveRange(
                maximum,
                state.Workspaces.Count - maximum);

        if (state.Workspaces.Count == 0)
            state.Workspaces.Add(CreateWorkspace(1));

        state.ActiveWorkspaceIndex = Math.Clamp(
            state.ActiveWorkspaceIndex,
            0,
            state.Workspaces.Count - 1);

        SaveState();
        RefreshWorkspaceCards();
    }

    private void SearchInput_TextChanged(
        object? sender,
        Avalonia.Controls.TextChangedEventArgs e)
    {
        RefreshWorkspaceCards();
    }

    // ---------------------------------------------------------
    // UI HELPERS
    // ---------------------------------------------------------

    private TextBlock CreateLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Brushes.LightGray
        };
    }

    private Border CreateDivider()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#41434D")),
            Margin = new Thickness(0, 5)
        };
    }

    private Border CreatePaneBorder()
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#30323B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#484A55")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 3)
        };
    }

    private Button CreateButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text
        };

        button.Click += (_, _) => action();

        return button;
    }

    private ComboBox CreateShellCombo(string? selected)
    {
        List<ShellProfile> profiles = ShellRegistry.Load();
        string? selectedId = ShellRegistry.Resolve(selected)?.Id;
        var combo = new ComboBox
        {
            ItemsSource = profiles,
            DisplayMemberBinding = new Binding(nameof(ShellProfile.Name)),
            SelectedItem = profiles.FirstOrDefault(profile =>
                profile.Id == selectedId),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        return combo;
    }

    // ---------------------------------------------------------
    // DIRECTORY PICKER
    // ---------------------------------------------------------

    private async Task<string?> BrowseDirectoryAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null)
            return null;

        IReadOnlyList<IStorageFolder> folders =
            await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Choose working directory",
                    AllowMultiple = false
                });

        if (folders.Count == 0)
            return null;

        return folders[0].Path.LocalPath;
    }
}
