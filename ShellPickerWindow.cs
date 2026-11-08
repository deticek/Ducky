using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ducky;

internal sealed class ShellPickerWindow : Window
{
    public ShellPickerWindow(IReadOnlyList<ShellProfile> profiles)
    {
        Title = "Choose terminal shell";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        MinHeight = 160;
        MaxHeight = 520;
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
            FontWeight = FontWeight.SemiBold
        });

        var choices = new StackPanel { Spacing = 6 };
        foreach (ShellProfile profile in profiles)
        {
            var button = new Button
            {
                Content = profile.Name,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 9)
            };
            button.Click += (_, _) => Close(profile);
            choices.Children.Add(button);
        }

        var scroll = new ScrollViewer
        {
            Content = choices,
            MaxHeight = 390,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);
        Content = root;

        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Close(null);
                args.Handled = true;
            }
        };
    }
}
