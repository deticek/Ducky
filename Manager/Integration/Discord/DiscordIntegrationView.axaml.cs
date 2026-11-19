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
            RefreshServerSettings();
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

    private void AddServer_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string serverId = (ServerIdTextBox.Text ?? "").Trim();
            if (!ulong.TryParse(serverId, out ulong numericId) || numericId == 0)
            {
                ServerSettingsStatusText.Text =
                    "Enter a valid numeric Discord server ID.";
                return;
            }

            Dictionary<string, bool> settings =
                DiscordCommandAccess.LoadServerSettings();
            string normalizedId = numericId.ToString();
            if (!settings.ContainsKey(normalizedId))
            {
                settings.Add(normalizedId, true);
                DiscordCommandAccess.SaveServerSettings(settings);
            }

            ServerIdTextBox.Text = "";
            RefreshServerSettings();
            ServerSettingsStatusText.Text =
                "Server added. /command is currently enabled there.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not add Discord server setting: " + ex);
            ServerSettingsStatusText.Text = "Could not add server: " + ex.Message;
        }
    }

    private void RefreshServerSettings()
    {
        ServerSettingsPanel.Children.Clear();
        foreach (KeyValuePair<string, bool> server in
                 DiscordCommandAccess.LoadServerSettings().OrderBy(
                     entry => entry.Key,
                     StringComparer.Ordinal))
        {
            string serverId = server.Key;
            CheckBox enabledCheckBox = new()
            {
                Content = $"{serverId} — /command enabled",
                IsChecked = server.Value,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            enabledCheckBox.IsCheckedChanged += (_, _) =>
            {
                try
                {
                    Dictionary<string, bool> settings =
                        DiscordCommandAccess.LoadServerSettings();
                    settings[serverId] = enabledCheckBox.IsChecked == true;
                    DiscordCommandAccess.SaveServerSettings(settings);
                    ServerSettingsStatusText.Text =
                        enabledCheckBox.IsChecked == true
                            ? $"/command enabled for server {serverId}."
                            : $"/command disabled for server {serverId}.";
                }
                catch (Exception ex)
                {
                    DuckyLog.Log(
                        "Could not update Discord server command setting: " + ex);
                    ServerSettingsStatusText.Text =
                        "Could not update server setting: " + ex.Message;
                }
            };

            Button removeButton = new()
            {
                Content = "Remove",
                Padding = new Avalonia.Thickness(12, 6)
            };
            removeButton.Click += (_, _) => RemoveServerSetting(serverId);

            StackPanel row = new()
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10
            };
            row.Children.Add(enabledCheckBox);
            row.Children.Add(removeButton);
            ServerSettingsPanel.Children.Add(row);
        }
    }

    private void RemoveServerSetting(string serverId)
    {
        try
        {
            Dictionary<string, bool> settings =
                DiscordCommandAccess.LoadServerSettings();
            settings.Remove(serverId);
            DiscordCommandAccess.SaveServerSettings(settings);
            RefreshServerSettings();
            ServerSettingsStatusText.Text =
                $"Override removed. /command is enabled by default for server {serverId}.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not remove Discord server setting: " + ex);
            ServerSettingsStatusText.Text =
                "Could not remove server setting: " + ex.Message;
        }
    }
}
