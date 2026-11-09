
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ducky;

internal sealed class ShellPickerWindow : Window
{
    private readonly List<ShellProfile> profiles = new();
    private readonly List<Button> buttons = new();

    private int selectedIndex = 0;

    public ShellPickerWindow(IReadOnlyList<ShellProfile> shellProfiles)
    {
        Title = "Choose terminal shell";
        Width = 360;
        Height = 200;
        MinHeight = 160;
        MaxHeight = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#202127"));

        var root = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10
        };

        root.Children.Add(new TextBlock
        {
            Text = "Choose a shell for this pane",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White
        });

        var list = new StackPanel
        {
            Spacing = 5
        };

        foreach (ShellProfile profile in shellProfiles)
        {
            profiles.Add(profile);

            var button = new Button
            {
                Content = profile.Name,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 10),
                Background = new SolidColorBrush(Color.Parse("#30313A")),
                Foreground = Brushes.White
            };

            button.Click += (_, _) =>
            {
                Close(profile);
            };

            buttons.Add(button);
            list.Children.Add(button);
        }

        root.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 390
        });

        Content = root;

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);

        Opened += (_, _) =>
        {
            if (buttons.Count > 0)
            {
                UpdateSelection();
                buttons[selectedIndex].Focus();
            }
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
            return;
        }

        if (buttons.Count == 0)
            return;

        if (e.Key == Key.Down)
        {
            if (selectedIndex < buttons.Count - 1)
                selectedIndex++;

            UpdateSelection();
            buttons[selectedIndex].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (selectedIndex > 0)
                selectedIndex--;

            UpdateSelection();
            buttons[selectedIndex].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Close(profiles[selectedIndex]);
            e.Handled = true;
        }
    }

    private void UpdateSelection()
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            if (i == selectedIndex)
                buttons[i].Background = new SolidColorBrush(Color.Parse("#50536A"));
            else
                buttons[i].Background = new SolidColorBrush(Color.Parse("#30313A"));
        }
    }
}
