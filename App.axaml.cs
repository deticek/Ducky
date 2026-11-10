using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ducky.Discord;
using System.Threading.Tasks;
using System;

namespace ducky;

public partial class App : Application
{

    private DiscordBotService? discordBot;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            if (Program.StartInManagerMode)
            {
                mainWindow.Opened += (_, _) => MainWindow.ShowDuckyManager();
            }

            discordBot = new DiscordBotService();

            _ = StartDiscordBotAsync();

            desktop.Exit += async (_, _) =>
            {
                if (discordBot is not null)
                    await discordBot.StopAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartDiscordBotAsync()
    {
        try
        {
            await discordBot!.StartAsync();
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Discord bot startup failed: " + ex);
        }
    }
}