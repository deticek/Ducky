using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;

namespace ducky;

internal enum SplitOrientation
{
    Vertical,
    Horizontal
}

internal sealed class WorkspaceLayoutState
{
    public WorkspacePaneState? Root { get; set; }
    public int ActivePaneIndex { get; set; }
}

internal sealed class WorkspacePaneState
{
    public string? WorkingDirectory { get; set; }
    public string? Orientation { get; set; }
    public WorkspacePaneState? First { get; set; }
    public WorkspacePaneState? Second { get; set; }
}

internal sealed class WorkspaceLayout
{
    private WorkspacePane? root;

    public Grid View { get; } = new();
    public TerminalView? ActiveTerminal { get; set; }
    public IReadOnlyList<TerminalView> Terminals => root?.GetTerminals() ?? [];

    public void Initialize(TerminalView terminal)
    {
        root = new WorkspacePane(terminal);
        View.Children.Add(root.View);
    }

    public void Restore(
        WorkspaceLayoutState state,
        Func<string?, TerminalView> createTerminal)
    {
        if (state.Root is null)
            throw new InvalidDataException("Workspace layout has no root pane.");

        root = WorkspacePane.Restore(state.Root, createTerminal);
        View.Children.Add(root.View);

        IReadOnlyList<TerminalView> terminals = Terminals;
        if (terminals.Count == 0)
            throw new InvalidDataException("Workspace layout has no terminal panes.");

        int activeIndex = Math.Clamp(state.ActivePaneIndex, 0, terminals.Count - 1);
        ActiveTerminal = terminals[activeIndex];
    }

    public WorkspaceLayoutState Save()
    {
        if (root is null)
            throw new InvalidOperationException("Workspace layout is not initialized.");

        int activeIndex = 0;
        IReadOnlyList<TerminalView> terminals = Terminals;
        for (int index = 0; index < terminals.Count; index++)
        {
            if (ReferenceEquals(terminals[index], ActiveTerminal))
            {
                activeIndex = index;
                break;
            }
        }

        return new WorkspaceLayoutState
        {
            Root = root.Save(),
            ActivePaneIndex = activeIndex
        };
    }

    public bool Split(
        TerminalView target,
        TerminalView newTerminal,
        SplitOrientation orientation)
    {
        return root?.Split(target, newTerminal, orientation) ?? false;
    }

    public bool ClosePane(TerminalView terminal)
    {
        if (root is null || !root.ClosePane(terminal))
            return false;

        terminal.Shutdown();
        return true;
    }

    public bool Contains(TerminalView? terminal) =>
        terminal is not null && root?.Contains(terminal) == true;

    public TerminalView? GetFirstTerminal() => root?.GetFirstTerminal();

    public void Shutdown()
    {
        if (root is null)
            return;

        foreach (TerminalView terminal in root.GetTerminals())
            terminal.Shutdown();
    }
}

internal sealed class WorkspacePane
{
    private TerminalView? terminal;
    private WorkspacePane? first;
    private WorkspacePane? second;
    private SplitOrientation orientation;

    public Grid View { get; } = new();

    public WorkspacePane()
    {
    }

    public WorkspacePane(TerminalView terminal)
    {
        this.terminal = terminal;
        View.Children.Add(terminal);
    }

    public static WorkspacePane Restore(
        WorkspacePaneState state,
        Func<string?, TerminalView> createTerminal)
    {
        if (state.Orientation is null)
            return new WorkspacePane(createTerminal(state.WorkingDirectory));

        if (!Enum.TryParse(state.Orientation, out SplitOrientation orientation) ||
            !Enum.IsDefined(orientation) ||
            state.First is null ||
            state.Second is null)
        {
            throw new InvalidDataException("Workspace split state is invalid.");
        }

        var pane = new WorkspacePane
        {
            orientation = orientation,
            first = Restore(state.First, createTerminal),
            second = Restore(state.Second, createTerminal)
        };
        pane.RefreshView();
        return pane;
    }

    public WorkspacePaneState Save()
    {
        if (terminal is not null)
        {
            return new WorkspacePaneState
            {
                WorkingDirectory = terminal.WorkingDirectory
            };
        }

        return new WorkspacePaneState
        {
            Orientation = orientation.ToString(),
            First = first!.Save(),
            Second = second!.Save()
        };
    }

    public bool Split(
        TerminalView target,
        TerminalView newTerminal,
        SplitOrientation splitOrientation)
    {
        WorkspacePane? pane = Find(target);
        if (pane is null)
            return false;

        pane.View.Children.Clear();
        pane.terminal = null;
        pane.orientation = splitOrientation;
        pane.first = new WorkspacePane(target);
        pane.second = new WorkspacePane(newTerminal);
        pane.RefreshView();
        return true;
    }

    public bool ClosePane(TerminalView target)
    {
        if (terminal is not null)
        {
            if (!ReferenceEquals(terminal, target))
                return false;

            terminal = null;
            View.Children.Clear();
            return true;
        }

        if (first!.ClosePane(target))
        {
            if (first.IsEmpty)
                Adopt(second!);
            else
                RefreshView();

            return true;
        }

        if (second!.ClosePane(target))
        {
            if (second.IsEmpty)
                Adopt(first!);
            else
                RefreshView();

            return true;
        }

        return false;
    }

    public bool Contains(TerminalView target)
    {
        if (terminal is not null)
            return ReferenceEquals(terminal, target);

        return first!.Contains(target) || second!.Contains(target);
    }

    public TerminalView? GetFirstTerminal()
    {
        if (terminal is not null)
            return terminal;

        return first!.GetFirstTerminal();
    }

    public IReadOnlyList<TerminalView> GetTerminals()
    {
        var terminals = new List<TerminalView>();
        AddTerminals(terminals);
        return terminals;
    }

    private void AddTerminals(List<TerminalView> terminals)
    {
        if (terminal is not null)
        {
            terminals.Add(terminal);
            return;
        }

        first!.AddTerminals(terminals);
        second!.AddTerminals(terminals);
    }

    private WorkspacePane? Find(TerminalView target)
    {
        if (terminal is not null)
            return ReferenceEquals(terminal, target) ? this : null;

        return first!.Find(target) ?? second!.Find(target);
    }

    private bool IsEmpty =>
        terminal is null && first is null && second is null;

    private void Adopt(WorkspacePane remaining)
    {
        remaining.View.Children.Clear();
        View.Children.Clear();

        terminal = remaining.terminal;
        first = remaining.first;
        second = remaining.second;
        orientation = remaining.orientation;
        RefreshView();
    }

    private void RefreshView()
    {
        View.Children.Clear();
        View.ColumnDefinitions.Clear();
        View.RowDefinitions.Clear();

        if (terminal is not null)
        {
            View.Children.Add(terminal);
            return;
        }

        if (orientation == SplitOrientation.Vertical)
        {
            View.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            View.ColumnDefinitions.Add(new ColumnDefinition(5, GridUnitType.Pixel));
            View.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            Grid.SetColumn(first!.View, 0);
            View.Children.Add(first.View);
            AddSplitter(GridResizeDirection.Columns, 1);
            Grid.SetColumn(second!.View, 2);
            View.Children.Add(second.View);
        }
        else
        {
            View.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            View.RowDefinitions.Add(new RowDefinition(5, GridUnitType.Pixel));
            View.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

            Grid.SetRow(first!.View, 0);
            View.Children.Add(first.View);
            AddSplitter(GridResizeDirection.Rows, 1);
            Grid.SetRow(second!.View, 2);
            View.Children.Add(second.View);
        }
    }

    private void AddSplitter(GridResizeDirection direction, int index)
    {
        var splitter = new GridSplitter
        {
            Background = new SolidColorBrush(Color.Parse("#505050")),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            ResizeDirection = direction
        };

        if (direction == GridResizeDirection.Columns)
        {
            splitter.Width = 5;
            Grid.SetColumn(splitter, index);
        }
        else
        {
            splitter.Height = 5;
            Grid.SetRow(splitter, index);
        }

        View.Children.Add(splitter);
    }
}
