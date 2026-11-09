
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ducky;

public partial class DuckyManager : Window
{
    private static DuckyManager? instance;
    private ThemeData? managerTheme;
    private ManagerSettingsData managerSettings;
    private bool closePromptOpen;
    private bool closeConfirmed;

    public DuckyManager()
    {
        InitializeComponent();

        instance = this;
        managerSettings = ManagerSettingsStore.Load();
        managerTheme = ThemeService.GetTheme(managerSettings.Theme);
        ApplyPreferences();
        ApplyManagerTheme();
        Opened += (_, _) => ApplyManagerTheme();
        SizeChanged += (_, _) => ApplySidebarBehavior();
        ThemeService.Changed += OnThemeChanged;
        Closed += (_, _) =>
        {
            ThemeService.Changed -= OnThemeChanged;
            if (ReferenceEquals(instance, this))
                instance = null;
        };

        PageContent.Content = new DashboardView();
        SetActiveButton(OverviewButton);
    }

    private void OnThemeChanged()
    {
        managerTheme = ThemeService.GetTheme(managerSettings.Theme);
        ApplyManagerTheme();
    }

    private void ApplyManagerTheme()
    {
        if (managerTheme is null)
            return;

        Color background = ParseColor(
            managerTheme.ManagerBackgroundColor,
            "#202127");
        Color sidebar = ParseColor(
            managerTheme.ManagerSidebarColor,
            "#252730");
        Color card = ParseColor(
            managerTheme.ManagerCardColor,
            "#252730");
        Color text = ParseColor(
            managerTheme.ManagerTextColor,
            "#F1F0EC");
        Color accent = ParseColor(
            managerTheme.ManagerAccentColor,
            "#EBDAC3");
        Color button = ParseColor(
            managerTheme.ManagerButtonColor,
            "#3B3D47");
        Color buttonText = ParseColor(
            managerTheme.ManagerButtonTextColor,
            "#F1F0EC");

        Background = new SolidColorBrush(background);
        ManagerLayout.Background = new SolidColorBrush(background);
        SidebarBorder.Background = new SolidColorBrush(sidebar);
        HeaderBar.Background = new SolidColorBrush(sidebar);
        LogoBadge.Background = new SolidColorBrush(accent);

        foreach (Control control in this.GetVisualDescendants())
        {
            if (control is Border border &&
                border.Classes.Contains("card"))
            {
                border.Background = new SolidColorBrush(card);
            }

            else if (control is Button buttonControl)
            {
                if (buttonControl.Classes.Contains("color-swatch"))
                    continue;

                bool isAccentButton =
                    buttonControl.Classes.Contains("primary-button") ||
                    buttonControl.Classes.Contains("action-button") ||
                    buttonControl.Classes.Contains("nav-active");
                buttonControl.Background = new SolidColorBrush(
                    isAccentButton ? accent : button);
                buttonControl.Foreground = new SolidColorBrush(
                    isAccentButton ? background : buttonText);
            }
            else if (control is TextBlock textBlock &&
                     !HasButtonAncestor(textBlock))
            {
                textBlock.Foreground = new SolidColorBrush(text);
            }
            else if (control is TextBox textBox)
            {
                textBox.Background = new SolidColorBrush(background);
                textBox.Foreground = new SolidColorBrush(text);
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.Background = new SolidColorBrush(background);
                comboBox.Foreground = new SolidColorBrush(text);
            }
            else if (control is NumericUpDown numericUpDown)
            {
                numericUpDown.Background = new SolidColorBrush(background);
                numericUpDown.Foreground = new SolidColorBrush(text);
            }
        }

    }

    internal static void ApplySettings(ManagerSettingsData settings)
    {
        if (instance is null)
            return;
    
        instance.managerSettings = settings;
        instance.managerTheme = ThemeService.GetTheme(settings.Theme);
        instance.ApplyPreferences();
        instance.ApplyManagerTheme();
    }

    private void ApplyPreferences()
    {
        double scale = Math.Clamp(managerSettings.UiScale, 80, 130) / 100d;

        Width = 1100 * scale;
        Height = 750 * scale;

        MinWidth = 850 * scale;
        MinHeight = 600 * scale;

        ManagerLayout.RenderTransform = null;

        ApplySidebarBehavior();
        ApplySidebarOrder();
    }

    private void ApplySidebarBehavior()
    {
        bool collapsed = managerSettings.SidebarBehavior == "Collapsed" ||
            (managerSettings.SidebarBehavior == "Auto" && Bounds.Width < 950);
        double width = collapsed ? 68 : 230;
        ManagerLayout.ColumnDefinitions[0].Width = new GridLength(width);
        SidebarBorder.Width = width;
        LogoTextPanel.IsVisible = !collapsed;
        GeneralLabel.IsVisible = !collapsed;

        foreach (Button button in NavigationPanel.Children.OfType<Button>()
                     .Append(SettingsButton))
        {
            button.HorizontalContentAlignment = collapsed
                ? Avalonia.Layout.HorizontalAlignment.Center
                : Avalonia.Layout.HorizontalAlignment.Left;
            if (button.Content is not StackPanel content)
                continue;
            foreach (Control child in content.Children)
            {
                if (child is TextBlock textBlock && content.Children.IndexOf(child) > 0)
                    textBlock.IsVisible = !collapsed;
            }
        }
    }

    private void ApplySidebarOrder()
    {
        Dictionary<string, Button> buttons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Overview"] = OverviewButton,
            ["Workspaces"] = WorkspacesButton,
            ["Custom Commands"] = CommandButton,
            ["Configuration"] = TimerSettingsButton,
            ["Timer"] = UpdatesButton,
            ["Discord integration"] = DiscordButton,
            ["API integration"] = ApiButton,
            ["Logs"] = LogsButton,
            ["Updates"] = ConfigurationButton,
            ["Themes"] = ThemesButton,
            ["Shortcuts"] = ShortcutsButton
        };

        List<Button> ordered = new();
        HashSet<Button> added = new();
        foreach (string name in managerSettings.SidebarOrder ?? [])
        {
            if (buttons.TryGetValue(name, out Button? button) && added.Add(button))
                ordered.Add(button);
        }

        foreach (Button button in buttons.Values)
        {
            if (added.Add(button))
                ordered.Add(button);
        }

        foreach (Button button in buttons.Values)
            NavigationPanel.Children.Remove(button);

        int insertionIndex = 0;
        for (int index = 0; index < NavigationPanel.Children.Count; index++)
        {
            if (NavigationPanel.Children[index] is TextBlock { Text: "GENERAL" })
            {
                insertionIndex = index + 1;
                break;
            }
        }

        foreach (Button button in ordered)
            NavigationPanel.Children.Insert(insertionIndex++, button);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel ||
            !managerSettings.ConfirmBeforeClosing ||
            closeConfirmed ||
            closePromptOpen)
        {
            if (closePromptOpen)
                e.Cancel = true;
            return;
        }

        e.Cancel = true;
        closePromptOpen = true;
        _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        var dialog = new Window
        {
            Title = "Close Ducky Manager?",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var close = new Button { Content = "Close", MinWidth = 80 };
        cancel.Click += (_, _) => dialog.Close(false);
        close.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(close);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Are you sure you want to close Ducky Manager?" },
                buttons
            }
        };

        try
        {
            bool confirmed = await dialog.ShowDialog<bool>(this);
            closePromptOpen = false;
            if (confirmed)
            {
                closeConfirmed = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            closePromptOpen = false;
            DuckyLog.Log("Could not confirm Manager close: " + ex);
        }
    }

    private static bool HasButtonAncestor(Control control)
    {
        for (Control? parent = control.Parent as Control;
             parent is not null;
             parent = parent.Parent as Control)
        {
            if (parent is Button)
                return true;
        }

        return false;
    }

    private static Color ParseColor(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out Color color)
            ? color
            : Color.Parse(fallback);

    private void SetActiveButton(Button activeButton)
    {
        foreach (Button button in new[]
                 {
                     OverviewButton, WorkspacesButton, LogsButton,
                     CommandButton, ConfigurationButton, TimerSettingsButton,
                     UpdatesButton, DiscordButton,
                     ApiButton, ShortcutsButton, ThemesButton, SettingsButton
                 })
        {
            button.Classes.Remove("nav-active");
        }

        activeButton.Classes.Add("nav-active");
        ApplyManagerTheme();
    }

    private void Overview_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new DashboardView();
        SetActiveButton(OverviewButton);
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new Settings();
        SetActiveButton(SettingsButton);
    }

    private void Workspaces_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new WorkspaceManagerView();
        SetActiveButton(WorkspacesButton);
    }

    private void Logs_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new LogsView();
        SetActiveButton(LogsButton);
    }

    private void Command_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new CommandsView();
        SetActiveButton(CommandButton);
    }

    private void Configuration_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new ConfigView();
        if (sender is Button button)
            SetActiveButton(button);
    }

    private void Discord_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new LogsView();
        SetActiveButton(LogsButton);
    }

    private void Api_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new LogsView();
        SetActiveButton(LogsButton);
    }

    private void Shortcuts_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new ShortcutsManager();
        SetActiveButton(ShortcutsButton);
    }

    private void Themes_Click(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new ThemesManagerView();
        SetActiveButton(ThemesButton);
    }
}
