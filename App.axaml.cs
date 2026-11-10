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

            if (ConfigReder.GetConfig().DiscordBotIntegration)
                _ = StartDiscordBotAsync();

            desktop.Exit += (_, _) =>
            {
                try
                {
                    discordBot?.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    DuckyLog.Log("Discord bot shutdown failed: " + ex);
                }
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

    public async Task ApplyDiscordBotSettingsAsync(
        bool enabled,
        bool restart = false)
    {
        if (discordBot is null)
            throw new InvalidOperationException(
                "The Discord bot service is not initialized.");

        if (!enabled)
        {
            await discordBot.StopAsync();
            return;
        }

        if (restart)
            await discordBot.StopAsync();

        await discordBot.StartAsync();
    }
}