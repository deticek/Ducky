using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ducky.Discord;

namespace ducky;

public partial class DiscordIntegrationView : UserControl
{
    public DiscordIntegrationView()
    {
        InitializeComponent();
        try
        {
            Config config = ConfigReder.GetConfig();
            BotEnabledCheckBox.IsChecked = config.DiscordBotIntegration;
            BotTokenStatusText.Text = DiscordTokenStore.HasToken
                ? "A saved token is present."
                : "No saved token. You can also use DISCORD_TOKEN from .env.";
            AllowedUserIdsTextBox.Text = string.Join(
                Environment.NewLine,
                DiscordCommandAccess.LoadAllowedUserIds().OrderBy(id => id));
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void BotEnabled_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            bool enabled = BotEnabledCheckBox.IsChecked == true;
            SaveIntegrationSetting(enabled);
            MainWindow.ReloadConfigFromDisk();
            await GetApp().ApplyDiscordBotSettingsAsync(enabled);
            BotTokenStatusText.Text = enabled
                ? "Discord bot is enabled."
                : "Discord bot is stopped.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not update Discord bot integration: " + ex);
            BotTokenStatusText.Text = "Could not update bot: " + ex.Message;
        }
    }

    private async void SaveBotToken_Click(object? sender, RoutedEventArgs e)
    {
        string token = BotTokenBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(token))
        {
            BotTokenStatusText.Text = "Enter a bot token first.";
            return;
        }

        try
        {
            DiscordTokenStore.Save(token);
            BotTokenBox.Text = "";
            bool enabled = BotEnabledCheckBox.IsChecked == true;
            if (enabled)
                await GetApp().ApplyDiscordBotSettingsAsync(enabled, restart: true);

            BotTokenStatusText.Text = enabled
                ? "Token saved securely; Discord bot restarted."
                : "Token saved securely.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not save Discord bot token: " + ex);
            BotTokenStatusText.Text = "Could not save token: " + ex.Message;
        }
    }

    private static App GetApp() =>
        Avalonia.Application.Current as App ??
        throw new InvalidOperationException("Ducky application is not available.");

    private static void SaveIntegrationSetting(bool enabled)
    {
        string path = DuckyPaths.ConfigFilePath;
        List<string> lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();
        bool updated = false;
        for (int index = 0; index < lines.Count; index++)
        {
            int separator = lines[index].IndexOf('=');
            if (separator < 0 ||
                !lines[index][..separator].Trim().Equals(
                    "discord-bot-integration",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines[index] = "discord-bot-integration = " +
                           (enabled ? "true" : "false");
            updated = true;
        }

        if (!updated)
            lines.Add("discord-bot-integration = " + (enabled ? "true" : "false"));

        string temporaryPath = path + ".tmp";
        File.WriteAllLines(temporaryPath, lines);
        File.Move(temporaryPath, path, true);
    }

    private void SaveAllowlist_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string[] userIds = (AllowedUserIdsTextBox.Text ?? "")
                .Split(
                    new[] { '\r', '\n', ',', ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
            DiscordCommandAccess.SaveAllowedUserIds(userIds);
            StatusText.Text = "Allowlist saved.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not save Discord command allowlist: " + ex);
            StatusText.Text = "Could not save allowlist: " + ex.Message;
        }
    }
}
