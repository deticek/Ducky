using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace ducky;

public partial class ThemesManagerView : UserControl
{
    private List<ThemeData> themes = new();
    private ThemeData? selectedTheme;

    private readonly string fontsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ducky",
        "Fonts"
    );

    public ThemesManagerView()
    {
        InitializeComponent();

        AttachColorSwatchHandlers();
        Directory.CreateDirectory(fontsDirectory);

        LoadThemes();
    }

    // THEMES

    private void LoadThemes()
    {
        themes = ThemeService.LoadAll();

        RefreshThemeList();

        if (themes.Count > 0)
        {
            string? activeThemeId = ThemeService.GetTheme(null)?.Id;
            SelectTheme(
                themes.FirstOrDefault(theme => theme.Id == activeThemeId) ??
                themes[0]);
        }
    }

    private void RefreshThemeList()
    {
        ThemesList.Children.Clear();

        foreach (ThemeData theme in themes)
        {
            Button button = new()
            {
                Content = theme.IsBuiltInDefault
                    ? theme.Name + " (Default)"
                    : theme.Name,
                Tag = theme.Id,
                MinWidth = 110,
                Padding = new Thickness(16, 11),
                Margin = new Thickness(3),
                HorizontalContentAlignment =
                    Avalonia.Layout.HorizontalAlignment.Center
            };

            if (selectedTheme != null && selectedTheme.Id == theme.Id)
            {
                button.Background = new SolidColorBrush(
                    Color.Parse("#45434A")
                );
            }

            button.Click += SelectTheme_Click;

            ThemesList.Children.Add(button);
        }
    }

    private void SelectTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        string? id = button.Tag?.ToString();

        ThemeData? theme = themes.FirstOrDefault(t => t.Id == id);

        if (theme != null)
        {
            SelectTheme(theme);
        }
    }

    private void SelectTheme(ThemeData theme)
    {
        selectedTheme = theme;

        ThemeNameInput.Text = theme.Name;
        ThemeNameInput.IsEnabled = !theme.IsBuiltInDefault;

        BackgroundColorInput.Text = theme.BackgroundColor;
        TerminalOpacityInput.Value =
            (decimal)Math.Clamp(theme.TerminalOpacity, 0, 100);
        TextColorInput.Text = theme.TextColor;
        CursorColorInput.Text = theme.CursorColor;
        SelectionColorInput.Text = theme.SelectionColor;
        SelectionTextColorInput.Text = theme.SelectionTextColor;
        NavbarBackgroundColorInput.Text = theme.NavbarBackgroundColor;
        NavbarTitleTextColorInput.Text = theme.NavbarTitleTextColor;
        NavbarButtonColorInput.Text = theme.NavbarButtonColor;
        NavbarButtonTextColorInput.Text = theme.NavbarButtonTextColor;
        WorkspaceLabelBackgroundColorInput.Text =
            theme.WorkspaceLabelBackgroundColor;
        WorkspaceLabelTextColorInput.Text = theme.WorkspaceLabelTextColor;
        ManagerBackgroundColorInput.Text = theme.ManagerBackgroundColor;
        ManagerSidebarColorInput.Text = theme.ManagerSidebarColor;
        ManagerCardColorInput.Text = theme.ManagerCardColor;
        ManagerTextColorInput.Text = theme.ManagerTextColor;
        ManagerAccentColorInput.Text = theme.ManagerAccentColor;
        ManagerButtonColorInput.Text = theme.ManagerButtonColor;
        ManagerButtonTextColorInput.Text = theme.ManagerButtonTextColor;
        DeleteThemeButton.IsEnabled = !theme.IsBuiltInDefault;
        ProtectedThemeText.IsVisible = theme.IsBuiltInDefault;
        ThemeStatusText.Text = "";

        SelectFont(theme.FontFamily);

        FontSizeInput.Value = (decimal)theme.FontSize;
        LineHeightInput.Value = (decimal)theme.LineHeight;
        CursorThicknessInput.Value = (decimal)theme.CursorThickness;

        SelectComboBoxItem(FontWeightInput, theme.FontWeight);
        SelectComboBoxItem(CursorStyleInput, theme.CursorStyle);

        CursorBlinkInput.IsChecked = theme.CursorBlink;

        UpdateColorSwatches();
        UpdatePreview();
        RefreshThemeList();
    }

    private void CreateTheme_Click(object? sender, RoutedEventArgs e)
    {
        ThemeData theme = new()
        {
            Name = "New Theme"
        };

        ThemeStore.Save(theme);
        themes.Add(theme);
        ThemeService.NotifyThemeSaved(theme);

        SelectTheme(theme);
    }

    private void SaveTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedTheme == null)
            return;

        if (!selectedTheme.IsBuiltInDefault)
        {
            selectedTheme.Name = string.IsNullOrWhiteSpace(ThemeNameInput.Text)
                ? "New Theme"
                : ThemeNameInput.Text.Trim();
        }

        selectedTheme.BackgroundColor =
            GetColorValue(BackgroundColorInput, "#11111B");
        selectedTheme.TerminalOpacity = Math.Clamp(
            (double)(TerminalOpacityInput.Value ?? 100),
            0,
            100);

        selectedTheme.TextColor =
            GetColorValue(TextColorInput, "#FFFFFF");

        selectedTheme.CursorColor =
            GetColorValue(CursorColorInput, "#EBDAC3");

        selectedTheme.SelectionColor =
            GetColorValue(SelectionColorInput, "#45434A");
        selectedTheme.SelectionTextColor =
            GetColorValue(SelectionTextColorInput, "#FFFFFF");
        selectedTheme.NavbarBackgroundColor =
            GetColorValue(NavbarBackgroundColorInput, "#202020");
        selectedTheme.NavbarTitleTextColor =
            GetColorValue(NavbarTitleTextColorInput, "#FFFFFF");
        selectedTheme.NavbarButtonColor =
            GetColorValue(NavbarButtonColorInput, "#F4D03F");
        selectedTheme.NavbarButtonTextColor =
            GetColorValue(NavbarButtonTextColorInput, "#202020");
        selectedTheme.WorkspaceLabelBackgroundColor =
            GetColorValue(WorkspaceLabelBackgroundColorInput, "#11111B");
        selectedTheme.WorkspaceLabelTextColor =
            GetColorValue(WorkspaceLabelTextColorInput, "#FFFFFF");
        selectedTheme.ManagerBackgroundColor =
            GetColorValue(ManagerBackgroundColorInput, "#202127");
        selectedTheme.ManagerSidebarColor =
            GetColorValue(ManagerSidebarColorInput, "#252730");
        selectedTheme.ManagerCardColor =
            GetColorValue(ManagerCardColorInput, "#252730");
        selectedTheme.ManagerTextColor =
            GetColorValue(ManagerTextColorInput, "#F1F0EC");
        selectedTheme.ManagerAccentColor =
            GetColorValue(ManagerAccentColorInput, "#EBDAC3");
        selectedTheme.ManagerButtonColor =
            GetColorValue(ManagerButtonColorInput, "#3B3D47");
        selectedTheme.ManagerButtonTextColor =
            GetColorValue(ManagerButtonTextColorInput, "#F1F0EC");

        selectedTheme.FontFamily =
            GetSelectedComboBoxText(FontFamilyInput, "Cascadia Mono");

        selectedTheme.FontSize =
            (double)(FontSizeInput.Value ?? 15);

        selectedTheme.LineHeight =
            (double)(LineHeightInput.Value ?? 1);

        selectedTheme.CursorThickness =
            (double)(CursorThicknessInput.Value ?? 1);

        selectedTheme.FontWeight =
            GetSelectedComboBoxText(FontWeightInput, "Normal");

        selectedTheme.CursorStyle =
            GetSelectedComboBoxText(CursorStyleInput, "Block");

        selectedTheme.CursorBlink =
            CursorBlinkInput.IsChecked ?? true;

        ThemeStore.Save(selectedTheme);
        SyncColorInputsToTheme();
        ThemeService.NotifyThemeSaved(selectedTheme);

        UpdateColorSwatches();
        RefreshThemeList();
        UpdatePreview();
    }

    private void DeleteTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedTheme == null)
            return;

        if (selectedTheme.IsBuiltInDefault)
        {
            ThemeStatusText.Text = "The built-in default theme cannot be deleted.";
            return;
        }

        ThemeService.DeleteTheme(selectedTheme);

        themes.Remove(selectedTheme);
        selectedTheme = null;

        LoadThemes();
    }

    private void SetActiveTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedTheme == null)
            return;

        SaveTheme_Click(sender, e);
        ThemeService.SetDefaultTheme(selectedTheme.Id);
        ThemeStatusText.Text = selectedTheme.Name + " is now the default theme.";
    }

    // COLORS

    private void BackgroundColorPick_Click(object? sender, RoutedEventArgs e)
    {
        OpenColorPicker(BackgroundColorInput);
    }

    private void TextColorPick_Click(object? sender, RoutedEventArgs e)
    {
        OpenColorPicker(TextColorInput);
    }

    private void CursorColorPick_Click(object? sender, RoutedEventArgs e)
    {
        OpenColorPicker(CursorColorInput);
    }

    private void SelectionColorPick_Click(object? sender, RoutedEventArgs e)
    {
        OpenColorPicker(SelectionColorInput);
    }

    private void SelectionTextColorPick_Click(object? sender, RoutedEventArgs e)
    {
        OpenColorPicker(SelectionTextColorInput);
    }

    private void NavbarBackgroundColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(NavbarBackgroundColorInput);

    private void NavbarTitleTextColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(NavbarTitleTextColorInput);

    private void NavbarButtonColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(NavbarButtonColorInput);

    private void NavbarButtonTextColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(NavbarButtonTextColorInput);

    private void WorkspaceLabelBackgroundColorPick_Click(
        object? sender,
        RoutedEventArgs e) =>
        OpenColorPicker(WorkspaceLabelBackgroundColorInput);

    private void WorkspaceLabelTextColorPick_Click(
        object? sender,
        RoutedEventArgs e) =>
        OpenColorPicker(WorkspaceLabelTextColorInput);

    private void ManagerBackgroundColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(ManagerBackgroundColorInput);

    private void ManagerSidebarColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(ManagerSidebarColorInput);

    private void ManagerCardColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(ManagerCardColorInput);

    private void ManagerTextColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(ManagerTextColorInput);

    private void ManagerAccentColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(ManagerAccentColorInput);

    private void ManagerButtonColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(ManagerButtonColorInput);

    private void ManagerButtonTextColorPick_Click(object? sender, RoutedEventArgs e) =>
        OpenColorPicker(ManagerButtonTextColorInput);

    private async void OpenColorPicker(TextBox target)
    {
        Color initialColor = GetColor(target.Text, Colors.White);
    
        ColorView colorView = new()
        {
            Color = initialColor,
            Width = 360,
            Height = 440
        };
    
        Border preview = new()
        {
            Height = 45,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(initialColor),
            BorderBrush = new SolidColorBrush(Color.Parse("#383A43")),
            BorderThickness = new Thickness(1)
        };
    
        TextBox hexInput = new()
        {
            Text = initialColor.ToString(),
            IsReadOnly = true,
            FontSize = 14
        };
    
        colorView.ColorChanged += (_, args) =>
        {
            preview.Background = new SolidColorBrush(args.NewColor);
            hexInput.Text = args.NewColor.ToString();
        };
    
        Window pickerWindow = new()
        {
            Title = "Izberi barvo",
            Width = 420,
            Height = 680,
            MinWidth = 420,
            MinHeight = 680,
            MaxWidth = 420,
            MaxHeight = 680,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#202127"))
        };
    
        Button selectButton = new()
        {
            Content = "Izberi barvo",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#EBDAC3")),
            Foreground = new SolidColorBrush(Color.Parse("#333333")),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(13, 10)
        };
    
        Button cancelButton = new()
        {
            Content = "Prekliči",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
    
        selectButton.Click += (_, _) =>
        {
            pickerWindow.Close((Color?)colorView.Color);
        };
    
        cancelButton.Click += (_, _) =>
        {
            pickerWindow.Close((Color?)null);
        };
    
        StackPanel content = new()
        {
            Margin = new Thickness(20),
            Spacing = 12
        };
    
        content.Children.Add(new TextBlock
        {
            Text = "Choose color",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#F1F0EC"))
        });
    
        content.Children.Add(colorView);
    
        content.Children.Add(new TextBlock
        {
            Text = "HEX",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#92949F"))
        });
    
        content.Children.Add(hexInput);
        content.Children.Add(selectButton);
        content.Children.Add(preview);
        content.Children.Add(cancelButton);
    
        pickerWindow.Content = content;
    
        Window? owner = TopLevel.GetTopLevel(this) as Window;
    
        if (owner == null)
            return;
    
        Color? result = await pickerWindow.ShowDialog<Color?>(owner);
    
        if (result.HasValue)
        {
            target.Text = result.Value.ToString();
    
            UpdateColorSwatches();
            UpdatePreview();
        }
    }

    private void UpdateColorSwatches()
    {
        SetSwatchColor(BackgroundColorPickerButton, BackgroundColorInput.Text);
        SetSwatchColor(TextColorPickerButton, TextColorInput.Text);
        SetSwatchColor(CursorColorPickerButton, CursorColorInput.Text);
        SetSwatchColor(SelectionColorPickerButton, SelectionColorInput.Text);
        SetSwatchColor(SelectionTextColorPickerButton, SelectionTextColorInput.Text);
        SetSwatchColor(NavbarBackgroundColorPickerButton, NavbarBackgroundColorInput.Text);
        SetSwatchColor(NavbarTitleTextColorPickerButton, NavbarTitleTextColorInput.Text);
        SetSwatchColor(NavbarButtonColorPickerButton, NavbarButtonColorInput.Text);
        SetSwatchColor(NavbarButtonTextColorPickerButton, NavbarButtonTextColorInput.Text);
        SetSwatchColor(
            WorkspaceLabelBackgroundColorPickerButton,
            WorkspaceLabelBackgroundColorInput.Text);
        SetSwatchColor(
            WorkspaceLabelTextColorPickerButton,
            WorkspaceLabelTextColorInput.Text);
        SetSwatchColor(ManagerBackgroundColorPickerButton, ManagerBackgroundColorInput.Text);
        SetSwatchColor(ManagerSidebarColorPickerButton, ManagerSidebarColorInput.Text);
        SetSwatchColor(ManagerCardColorPickerButton, ManagerCardColorInput.Text);
        SetSwatchColor(ManagerTextColorPickerButton, ManagerTextColorInput.Text);
        SetSwatchColor(ManagerAccentColorPickerButton, ManagerAccentColorInput.Text);
        SetSwatchColor(ManagerButtonColorPickerButton, ManagerButtonColorInput.Text);
        SetSwatchColor(ManagerButtonTextColorPickerButton, ManagerButtonTextColorInput.Text);
    }

    private void SyncColorInputsToTheme()
    {
        if (selectedTheme is null)
            return;

        BackgroundColorInput.Text = selectedTheme.BackgroundColor;
        TextColorInput.Text = selectedTheme.TextColor;
        CursorColorInput.Text = selectedTheme.CursorColor;
        SelectionColorInput.Text = selectedTheme.SelectionColor;
        SelectionTextColorInput.Text = selectedTheme.SelectionTextColor;
        NavbarBackgroundColorInput.Text = selectedTheme.NavbarBackgroundColor;
        NavbarTitleTextColorInput.Text = selectedTheme.NavbarTitleTextColor;
        NavbarButtonColorInput.Text = selectedTheme.NavbarButtonColor;
        NavbarButtonTextColorInput.Text = selectedTheme.NavbarButtonTextColor;
        WorkspaceLabelBackgroundColorInput.Text =
            selectedTheme.WorkspaceLabelBackgroundColor;
        WorkspaceLabelTextColorInput.Text = selectedTheme.WorkspaceLabelTextColor;
        ManagerBackgroundColorInput.Text = selectedTheme.ManagerBackgroundColor;
        ManagerSidebarColorInput.Text = selectedTheme.ManagerSidebarColor;
        ManagerCardColorInput.Text = selectedTheme.ManagerCardColor;
        ManagerTextColorInput.Text = selectedTheme.ManagerTextColor;
        ManagerAccentColorInput.Text = selectedTheme.ManagerAccentColor;
        ManagerButtonColorInput.Text = selectedTheme.ManagerButtonColor;
        ManagerButtonTextColorInput.Text = selectedTheme.ManagerButtonTextColor;
    }

    private void AttachColorSwatchHandlers()
    {
        (TextBox Input, Button Swatch)[] colorInputs =
        {
            (BackgroundColorInput, BackgroundColorPickerButton),
            (TextColorInput, TextColorPickerButton),
            (CursorColorInput, CursorColorPickerButton),
            (SelectionColorInput, SelectionColorPickerButton),
            (SelectionTextColorInput, SelectionTextColorPickerButton),
            (NavbarBackgroundColorInput, NavbarBackgroundColorPickerButton),
            (NavbarTitleTextColorInput, NavbarTitleTextColorPickerButton),
            (NavbarButtonColorInput, NavbarButtonColorPickerButton),
            (NavbarButtonTextColorInput, NavbarButtonTextColorPickerButton),
            (WorkspaceLabelBackgroundColorInput, WorkspaceLabelBackgroundColorPickerButton),
            (WorkspaceLabelTextColorInput, WorkspaceLabelTextColorPickerButton),
            (ManagerBackgroundColorInput, ManagerBackgroundColorPickerButton),
            (ManagerSidebarColorInput, ManagerSidebarColorPickerButton),
            (ManagerCardColorInput, ManagerCardColorPickerButton),
            (ManagerTextColorInput, ManagerTextColorPickerButton),
            (ManagerAccentColorInput, ManagerAccentColorPickerButton),
            (ManagerButtonColorInput, ManagerButtonColorPickerButton),
            (ManagerButtonTextColorInput, ManagerButtonTextColorPickerButton)
        };

        foreach ((TextBox input, Button swatch) in colorInputs)
            input.TextChanged += (_, _) => SetSwatchColor(swatch, input.Text);
    }

    private void SetSwatchColor(Button button, string? value)
    {
        Color color = GetColor(value, Colors.Transparent);
        button.Background = new SolidColorBrush(color);
        button.BorderBrush = Brushes.Transparent;
    }

    private string GetColorValue(TextBox input, string fallback)
    {
        if (Color.TryParse(input.Text, out Color color))
            return color.ToString();

        return fallback;
    }

    private Color GetColor(string? value, Color fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Color.TryParse(value, out Color color))
        {
            return color;
        }

        return fallback;
    }

    // COMBOBOX HELPERS

    private void SelectComboBoxItem(ComboBox comboBox, string value)
    {
        foreach (object? item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                comboBoxItem.Content?.ToString() == value)
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private string GetSelectedComboBoxText(
        ComboBox comboBox,
        string fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Content?.ToString() ?? fallback;
        }

        return fallback;
    }

    private void SelectFont(string fontName)
    {
        foreach (object? item in FontFamilyInput.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                comboBoxItem.Content?.ToString() == fontName)
            {
                FontFamilyInput.SelectedItem = comboBoxItem;
                return;
            }
        }

        FontFamilyInput.SelectedIndex = 0;
    }

    // FONT IMPORT

    private async void ImportFont_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel == null)
            return;

        IReadOnlyList<IStorageFile> files =
            await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import font",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Font files")
                        {
                            Patterns = new[] { "*.ttf", "*.otf" }
                        }
                    }
                }
            );

        if (files.Count == 0)
            return;

        string sourcePath = files[0].Path.LocalPath;
        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (extension != ".ttf" && extension != ".otf")
            return;

        string fileName = Path.GetFileName(sourcePath);
        string destinationPath = Path.Combine(fontsDirectory, fileName);

        File.Copy(sourcePath, destinationPath, true);

        string fontName = Path.GetFileNameWithoutExtension(fileName);

        bool alreadyExists = false;

        foreach (object? item in FontFamilyInput.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                comboBoxItem.Content?.ToString() == fontName)
            {
                alreadyExists = true;
                break;
            }
        }

        if (!alreadyExists)
        {
            ComboBoxItem newFont = new()
            {
                Content = fontName
            };

            FontFamilyInput.Items.Add(newFont);
            FontFamilyInput.SelectedItem = newFont;
        }
    }

    // THEME IMPORT

    private async void ImportTheme_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel == null)
            return;

        IReadOnlyList<IStorageFile> files =
            await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import theme",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Theme files")
                        {
                            Patterns = new[] { "*.conf" }
                        }
                    }
                }
            );

        if (files.Count == 0)
            return;

        string content = await File.ReadAllTextAsync(
            files[0].Path.LocalPath
        );

        ThemeData theme = ParseTheme(content);

        ThemeStore.Save(theme);
        themes.Add(theme);
        ThemeService.NotifyThemeSaved(theme);

        SelectTheme(theme);
    }

    // THEME EXPORT

    private async void ExportTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedTheme == null)
            return;

        SaveTheme_Click(sender, e);

        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel == null)
            return;

        IStorageFile? file =
            await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export theme",
                    SuggestedFileName = selectedTheme.Name + ".conf",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Theme files")
                        {
                            Patterns = new[] { "*.conf" }
                        }
                    }
                }
            );

        if (file == null)
            return;

        string content = CreateThemeConfig(selectedTheme);

        await File.WriteAllTextAsync(
            file.Path.LocalPath,
            content
        );
    }

    private ThemeData ParseTheme(string content)
    {
        ThemeData theme = new();

        string[] lines = content.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );

        foreach (string line in lines)
        {
            string[] parts = line.Split('=', 2);

            if (parts.Length != 2)
                continue;

            string key = parts[0].Trim();
            string value = parts[1].Trim();

            switch (key)
            {
                case "Name":
                    theme.Name = value;
                    break;

                case "BackgroundColor":
                    theme.BackgroundColor = value;
                    break;

                case "TerminalOpacity":
                    if (double.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        out double terminalOpacity))
                    {
                        theme.TerminalOpacity = Math.Clamp(terminalOpacity, 0, 100);
                    }
                    break;

                case "TextColor":
                    theme.TextColor = value;
                    break;

                case "CursorColor":
                    theme.CursorColor = value;
                    break;

                case "SelectionColor":
                    theme.SelectionColor = value;
                    break;

                case "SelectionTextColor":
                    theme.SelectionTextColor = value;
                    break;

                case "NavbarBackgroundColor":
                    theme.NavbarBackgroundColor = value;
                    break;

                case "NavbarTitleTextColor":
                    theme.NavbarTitleTextColor = value;
                    break;

                case "NavbarButtonColor":
                    theme.NavbarButtonColor = value;
                    break;

                case "NavbarButtonTextColor":
                    theme.NavbarButtonTextColor = value;
                    break;

                case "WorkspaceLabelBackgroundColor":
                    theme.WorkspaceLabelBackgroundColor = value;
                    break;

                case "WorkspaceLabelTextColor":
                    theme.WorkspaceLabelTextColor = value;
                    break;

                case "ManagerBackgroundColor":
                    theme.ManagerBackgroundColor = value;
                    break;

                case "ManagerSidebarColor":
                    theme.ManagerSidebarColor = value;
                    break;

                case "ManagerCardColor":
                    theme.ManagerCardColor = value;
                    break;

                case "ManagerTextColor":
                    theme.ManagerTextColor = value;
                    break;

                case "ManagerAccentColor":
                    theme.ManagerAccentColor = value;
                    break;

                case "ManagerButtonColor":
                    theme.ManagerButtonColor = value;
                    break;

                case "ManagerButtonTextColor":
                    theme.ManagerButtonTextColor = value;
                    break;

                case "FontFamily":
                    theme.FontFamily = value;
                    break;

                case "FontSize":
                    if (double.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        out double fontSize))
                    {
                        theme.FontSize = fontSize;
                    }
                    break;

                case "LineHeight":
                    if (double.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        out double lineHeight))
                    {
                        theme.LineHeight = lineHeight;
                    }
                    break;

                case "FontWeight":
                    theme.FontWeight = value;
                    break;

                case "CursorStyle":
                    theme.CursorStyle = value;
                    break;

                case "CursorBlink":
                    if (bool.TryParse(value, out bool blink))
                        theme.CursorBlink = blink;
                    break;

                case "CursorThickness":
                    if (double.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        out double thickness))
                    {
                        theme.CursorThickness = thickness;
                    }
                    break;
            }
        }

        return theme;
    }

    private string CreateThemeConfig(ThemeData theme)
    {
        return
            $"Name={theme.Name}\n" +
            $"BackgroundColor={theme.BackgroundColor}\n" +
            $"TerminalOpacity={theme.TerminalOpacity.ToString(CultureInfo.InvariantCulture)}\n" +
            $"TextColor={theme.TextColor}\n" +
            $"CursorColor={theme.CursorColor}\n" +
            $"SelectionColor={theme.SelectionColor}\n" +
            $"SelectionTextColor={theme.SelectionTextColor}\n" +
            $"NavbarBackgroundColor={theme.NavbarBackgroundColor}\n" +
            $"NavbarTitleTextColor={theme.NavbarTitleTextColor}\n" +
            $"NavbarButtonColor={theme.NavbarButtonColor}\n" +
            $"NavbarButtonTextColor={theme.NavbarButtonTextColor}\n" +
            $"WorkspaceLabelBackgroundColor={theme.WorkspaceLabelBackgroundColor}\n" +
            $"WorkspaceLabelTextColor={theme.WorkspaceLabelTextColor}\n" +
            $"ManagerBackgroundColor={theme.ManagerBackgroundColor}\n" +
            $"ManagerSidebarColor={theme.ManagerSidebarColor}\n" +
            $"ManagerCardColor={theme.ManagerCardColor}\n" +
            $"ManagerTextColor={theme.ManagerTextColor}\n" +
            $"ManagerAccentColor={theme.ManagerAccentColor}\n" +
            $"ManagerButtonColor={theme.ManagerButtonColor}\n" +
            $"ManagerButtonTextColor={theme.ManagerButtonTextColor}\n" +
            $"FontFamily={theme.FontFamily}\n" +
            $"FontSize={theme.FontSize.ToString(CultureInfo.InvariantCulture)}\n" +
            $"LineHeight={theme.LineHeight.ToString(CultureInfo.InvariantCulture)}\n" +
            $"FontWeight={theme.FontWeight}\n" +
            $"CursorStyle={theme.CursorStyle}\n" +
            $"CursorBlink={theme.CursorBlink}\n" +
            $"CursorThickness={theme.CursorThickness.ToString(CultureInfo.InvariantCulture)}\n";
    }

    // LIVE PREVIEW

    private void UpdatePreview()
    {
        if (ThemePreview == null)
            return;

        Color background = GetColor(
            BackgroundColorInput.Text,
            Colors.Black
        );
        background = Color.FromArgb(
            (byte)Math.Round(
                background.A *
                (double)(TerminalOpacityInput.Value ?? 100) / 100),
            background.R,
            background.G,
            background.B);

        Color text = GetColor(
            TextColorInput.Text,
            Colors.White
        );

        Color cursor = GetColor(
            CursorColorInput.Text,
            Colors.White
        );

        ThemePreview.Background = new SolidColorBrush(background);

        PreviewPath.Foreground = new SolidColorBrush(text);
        PreviewCommand.Foreground = new SolidColorBrush(text);
        PreviewOutput.Foreground = new SolidColorBrush(text);
        PreviewSuccess.Foreground = new SolidColorBrush(text);
        PreviewCursor.Foreground = new SolidColorBrush(cursor);
        PreviewSelection.Background = new SolidColorBrush(
            GetColor(SelectionColorInput.Text, Color.Parse("#45475A")));
        PreviewSelection.Foreground = new SolidColorBrush(
            GetColor(SelectionTextColorInput.Text, Colors.White));

        string fontName = GetSelectedComboBoxText(
            FontFamilyInput,
            "Consolas"
        );

        FontFamily fontFamily = new(fontName);

        double fontSize = (double)(FontSizeInput.Value ?? 15);
        double lineHeight = (double)(LineHeightInput.Value ?? 1);

        FontWeight fontWeight =
            GetSelectedComboBoxText(FontWeightInput, "Normal") switch
            {
                "Bold" => FontWeight.Bold,
                "Light" => FontWeight.Light,
                _ => FontWeight.Normal
            };

        TextBlock[] previewTexts =
        {
            PreviewPath,
            PreviewCommand,
            PreviewOutput,
            PreviewSuccess,
            PreviewCursor
        };

        foreach (TextBlock textBlock in previewTexts)
        {
            textBlock.FontFamily = fontFamily;
            textBlock.FontSize = fontSize;
            textBlock.FontWeight = fontWeight;
            textBlock.LineHeight = fontSize * lineHeight;
        }
    }
}