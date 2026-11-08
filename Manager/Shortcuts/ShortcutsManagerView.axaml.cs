using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace ducky;

public partial class ShortcutsManager : UserControl
{
    private Config config = new();
    private ShortcutDefinition? captureTarget;

    public ShortcutsManager()
    {
        InitializeComponent();
        RefreshShortcuts();
    }

    private void RefreshShortcuts()
    {
        config = ConfigReder.GetConfig();
        ShortcutsPanel.Children.Clear();

        foreach (ShortcutDefinition definition in ShortcutService.Definitions)
        {
            string value = definition.GetValue(config);
            string displayedValue = string.IsNullOrWhiteSpace(value) ||
                                    value.Equals("null", StringComparison.OrdinalIgnoreCase)
                ? "Disabled"
                : value;

            var title = new TextBlock
            {
                Text = definition.Name,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var binding = new Border
            {
                Background = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#30323B")),
                CornerRadius = new Avalonia.CornerRadius(5),
                Padding = new Avalonia.Thickness(10, 6),
                MinWidth = 100,
                Child = new TextBlock
                {
                    Text = displayedValue,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };

            var changeButton = new Button
            {
                Content = "Change",
                Classes = { "primary" }
            };
            changeButton.Click += (_, _) => StartCapture(definition);

            var resetButton = new Button
            {
                Content = "Reset"
            };
            resetButton.Click += (_, _) =>
                SaveBinding(definition, definition.DefaultValue);

            var deleteButton = new Button
            {
                Content = "Delete",
                Classes = { "danger" }
            };
            deleteButton.Click += (_, _) => SaveBinding(definition, "null");

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
                ColumnSpacing = 8,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            row.Children.Add(title);
            Grid.SetColumn(binding, 1);
            row.Children.Add(binding);
            Grid.SetColumn(changeButton, 2);
            row.Children.Add(changeButton);
            Grid.SetColumn(resetButton, 3);
            row.Children.Add(resetButton);
            Grid.SetColumn(deleteButton, 4);
            row.Children.Add(deleteButton);

            ShortcutsPanel.Children.Add(new Border
            {
                Classes = { "shortcut-card" },
                Child = row
            });
        }
    }

    private void StartCapture(ShortcutDefinition definition)
    {
        captureTarget = definition;
        CapturePreview.Text = "Waiting for input...";
        CaptureOverlay.IsVisible = true;
        CaptureOverlay.Focus();
    }

    private void CaptureOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            CaptureOverlay.IsVisible = false;
            captureTarget = null;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin)
        {
            return;
        }

        string binding = ShortcutService.FormatBinding(e.Key, e.KeyModifiers);
        CapturePreview.Text = binding;

        if (captureTarget is not null)
            SaveBinding(captureTarget, binding);
    }

    private void SaveBinding(ShortcutDefinition definition, string value)
    {
        try
        {
            ShortcutService.SaveBinding(definition, value);
            MainWindow.ReloadConfigFromDisk();
            StatusText.Text = value == "null"
                ? definition.Name + " disabled."
                : definition.Name + " saved as " + value + ".";
            CaptureOverlay.IsVisible = false;
            captureTarget = null;
            RefreshShortcuts();
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not save keyboard shortcut: " + ex);
            StatusText.Text = "Could not save shortcut: " + ex.Message;
        }
    }
}