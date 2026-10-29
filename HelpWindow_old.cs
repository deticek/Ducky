using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace test;

public static class OldHelpWindow
{

    private static int width = 27;

    private static string CalculateTitle(string title){

        string back = "\n";

        int other = (width-(title.Length + 2)) / 2;

        for(int i=0;i!=2;i++){
            for(int j=0;j!=other;j++){
                back+="=";
            }
            if(i==0)back+=$" {title} ";
        }

        return back;
    }

    private static string CalculateLane(string key, string info){

        string back = "";

        int space = width - (key.Length + info.Length);

        back += key;
        
        for(int i=0;i!=space;i++){
            back+=" ";
        }
        back+=info;

        return back;

    }

    public static void Show(MainWindow mainWindow, Config config)
    {

        width = Math.Max(27,config.HelpWindowWidth);
        //Alert.Info(width + " " + config.HelpWindowWidth);

        double helpWidth = Math.Max(27, config.HelpWindowWidth);
        double windowWidth = helpWidth * 8 + 60;

        List<string> lines = new List<string>();

        lines.Add(CalculateTitle("TERMINAL"));
        lines.Add(CalculateLane(config.HelpWindow,"Show this help"));
        lines.Add(CalculateLane(config.IncreaseFontSize,"Increase font size"));
        lines.Add(CalculateLane(config.DecreseFontSize,"Decrease font size"));
        lines.Add(CalculateLane("alt+f4","Exit"));
        
        lines.Add(CalculateTitle("PANE"));
        lines.Add(CalculateLane(config.TerminalVerticalSplit,"Split pane vertical"));
        lines.Add(CalculateLane(config.TerminalHorizontalSplit,"Split pane horizontal"));
        lines.Add(CalculateLane(config.TerminalCloseSplit,"Close active pane"));
        lines.Add(CalculateLane(config.TerminalNextWorkspace,"Next pane"));
        lines.Add(CalculateLane(config.TerminalPreviousWorkspace,"Previous pane"));
        
        lines.Add(CalculateTitle("WORKSPACES"));
        lines.Add(CalculateLane(config.Workspace1,"Move to workspace 1"));
        lines.Add(CalculateLane(config.Workspace2,"Move to workspace 2"));
        lines.Add(CalculateLane(config.Workspace3,"Move to workspace 3"));
        lines.Add(CalculateLane(config.Workspace4,"Move to workspace 4"));
        lines.Add(CalculateLane(config.Workspace5,"Move to workspace 5"));
        lines.Add(CalculateLane(config.Workspace6,"Move to workspace 6"));
        lines.Add(CalculateLane(config.Workspace7,"Move to workspace 7"));
        lines.Add(CalculateLane(config.Workspace8,"Move to workspace 8"));
        lines.Add(CalculateLane(config.Workspace9,"Move to workspace 9"));
        lines.Add(CalculateLane(config.Workspace10,"Move to workspace 10"));
        lines.Add(CalculateLane(config.TerminalNextWorkspace,"Next workspace"));
        lines.Add(CalculateLane(config.TerminalPreviousWorkspace,"Previus workspace"));

        var helpWindow = new Window
        {
            Title = "Keyboard shortcuts",
            SizeToContent = SizeToContent.Height,
            Width = windowWidth,
            MinWidth = windowWidth,
            MinHeight = 600,
            MaxHeight = 800,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = mainWindow.Background,
            Foreground = mainWindow.Foreground,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20, 20, 20, 10),
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "KEYBOARD SHORTCUTS",
                        FontSize = 20,
                        HorizontalAlignment =
                            Avalonia.Layout.HorizontalAlignment.Center,
                        FontWeight = FontWeight.Bold,
                        Foreground = mainWindow.Foreground,
                        Margin = new Thickness(0, 0, 0, 8)
                    },

                    new TextBlock
                    {
                        Text = string.Join("\n", lines),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 14,
                        Foreground = mainWindow.Foreground
                    },

                    new Button
                    {
                        Content = "Close",
                        HorizontalAlignment =
                            Avalonia.Layout.HorizontalAlignment.Center,
                        Padding = new Thickness(20, 8)
                    }
                }
            }
        };

        helpWindow.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape ||
                e.Key == Key.F1 ||
                (e.Key == Key.W &&
                 e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                helpWindow.Close();
                e.Handled = true;
            }
        };

        if (helpWindow.Content is StackPanel panel &&
            panel.Children[2] is Button closeButton)
        {
            closeButton.Click += (_, _) => helpWindow.Close();
        }

        helpWindow.ShowDialog(mainWindow);
    }
}