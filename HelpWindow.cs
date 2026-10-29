
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace test;

public static class HelpWindow
{
    private static readonly IBrush BackgroundBrush =
        Brush("#17191F");

    private static readonly IBrush SurfaceBrush =
        Brush("#24262E");

    private static readonly IBrush KeyBrush =
        Brush("#30323B");

    private static readonly IBrush AccentBrush =
        Brush("#F4D03F");

    private static readonly IBrush TextBrush =
        Brush("#F4F4F5");

    private static readonly IBrush MutedBrush =
        Brush("#A1A1AA");

    private static readonly IBrush BorderBrush =
        Brush("#393B44");

    private static SolidColorBrush Brush(string color) =>
        new(Color.Parse(color));

    public static void Show(MainWindow mainWindow, Config config)
    {
        var content = new StackPanel
        {
            Spacing = 0
        };

        // Header
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var iconBox = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = AccentBrush,
            Child = new TextBlock
            {
                Text = "⌨",
                FontSize = 25,
                Foreground = BackgroundBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var titleStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = "Ducky Help",
                    FontSize = 21,
                    FontWeight = FontWeight.Bold,
                    Foreground = TextBrush
                },
                new TextBlock
                {
                    Text = "Keyboard shortcuts",
                    FontSize = 13,
                    Foreground = MutedBrush
                }
            }
        };

        header.Children.Add(iconBox);
        header.Children.Add(titleStack);
        Grid.SetColumn(titleStack, 1);

        content.Children.Add(header);
        content.Children.Add(Divider());

        // Shortcut sections
        AddSection(content, "TERMINAL",
            (config.HelpWindow, "Show this help"),
            (config.TemporaryTerminal, "Open temporary terminal"),
            ("Alt + T", "Open theme list"),
            ("Alt + S", "Open shell list"),
            ("Alt + F", "Open font list"),
            (config.IncreaseFontSize, "Increase font size"),
            (config.DecreseFontSize, "Decrease font size"),
            ("Ctrl + R", "Reset path to home"),
            ("Alt + F4", "Exit"));

        AddSection(content, "PANES",
            (config.TerminalVerticalSplit, "Split pane vertically"),
            (config.TerminalHorizontalSplit, "Split pane horizontally"),
            (config.TerminalCloseSplit, "Close active pane"),
            (config.TerminalNextWorkspace, "Next pane"),
            (config.TerminalPreviousWorkspace, "Previous pane"));

        AddSection(content, "WORKSPACES",
            (config.Workspace1, "Move to workspace 1"),
            (config.Workspace2, "Move to workspace 2"),
            (config.Workspace3, "Move to workspace 3"),
            (config.Workspace4, "Move to workspace 4"),
            (config.Workspace5, "Move to workspace 5"),
            (config.Workspace6, "Move to workspace 6"),
            (config.Workspace7, "Move to workspace 7"),
            (config.Workspace8, "Move to workspace 8"),
            (config.Workspace9, "Move to workspace 9"),
            (config.Workspace10, "Move to workspace 10"),
            (config.TerminalNextWorkspace, "Next workspace"),
            (config.TerminalPreviousWorkspace, "Previous workspace"));

        // Footer
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 16, 0, 0)
        };

        var hint = new TextBlock
        {
            Text = "Press Esc to close",
            FontSize = 12,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button
        {
            Content = "Close",
            Padding = new Thickness(20, 9),
            Background = AccentBrush,
            Foreground = BackgroundBrush,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(
                Avalonia.Input.StandardCursorType.Hand)
        };

        footer.Children.Add(hint);
        footer.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 1);

        content.Children.Add(Divider());
        content.Children.Add(footer);

        // Window
        var helpWindow = new Window
        {
            Title = "Ducky Help",
            Width = 450,
            MinWidth = 400,
            MaxWidth = 560,
            Height = 680,
            MinHeight = 400,
            MaxHeight = 850,
            CanResize = false,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Background = BackgroundBrush,
            Foreground = TextBrush,
            Content = new Border
            {
                Padding = new Thickness(22),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility =
                        ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility =
                        ScrollBarVisibility.Disabled,
                    Content = new Border
                    {
                        Padding = new Thickness(0, 0, 25, 0),
                        Child = content
                    }
                }
            }
        };

        closeButton.Click += (_, _) => helpWindow.Close();

        helpWindow.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape ||
                e.Key == Avalonia.Input.Key.F1 ||
                (e.Key == Avalonia.Input.Key.W &&
                 e.KeyModifiers.HasFlag(
                     Avalonia.Input.KeyModifiers.Control)))
            {
                helpWindow.Close();
                e.Handled = true;
            }
        };

        helpWindow.ShowDialog(mainWindow);
    }

    private static void AddSection(
        StackPanel parent,
        string title,
        params (string Key, string Description)[] shortcuts)
    {
        var section = new StackPanel
        {
            Spacing = 9,
            Margin = new Thickness(0, 18, 0, 0)
        };

        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = AccentBrush,
            Margin = new Thickness(0, 0, 0, 3)
        });

        foreach (var shortcut in shortcuts)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 12,
                MinHeight = 32
            };

            row.Children.Add(new TextBlock
            {
                Text = shortcut.Description,
                FontSize = 13,
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            var key = new Border
            {
                Background = KeyBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 5),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = shortcut.Key,
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = TextBrush
                }
            };

            row.Children.Add(key);
            Grid.SetColumn(key, 1);
            section.Children.Add(row);
        }

        parent.Children.Add(section);
    }

    private static Control Divider() => new Border
    {
        Height = 1,
        Background = BorderBrush
    };
}
