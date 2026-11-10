
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;
using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;

using ducky.Discord.Commands;

namespace ducky.Discord;

public class DiscordBotService
{
    private IHost? host;
    private GatewayClient? gateway;
    private Semaphore? botLease;
    private CancellationTokenSource? startupCancellation;
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    public bool IsRunning => host is not null;

    public async Task StartAsync()
    {
        await lifecycleLock.WaitAsync();
        try
        {
            if (host is not null)
                return;

            using var startup = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));
            startupCancellation = startup;

            Env.TraversePath().Load();

            string? token = DiscordTokenStore.Load();
            if (string.IsNullOrWhiteSpace(token))
                token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "Discord bot token is missing. Add it in Manager > Discord integration.");
            }

            var builder = Host.CreateApplicationBuilder();
            builder.Services
                .AddDiscordGateway(options =>
                {
                    options.Token = token;
                })
                .AddApplicationCommands();

            IHost candidate = builder.Build();
            try
            {
                AcquireBotLease(token);
                candidate.AddModules(typeof(PingCommand).Assembly);
                await candidate.StartAsync(startup.Token);

                GatewayClient gateway =
                    candidate.Services.GetRequiredService<GatewayClient>();
                this.gateway = gateway;
                host = candidate;

                ApplicationCommandService<ApplicationCommandContext> commands =
                    candidate.Services.GetRequiredService<
                        ApplicationCommandService<ApplicationCommandContext>>();
                await commands.RegisterCommandsAsync(
                    gateway.Rest,
                    gateway.Id,
                    cancellationToken: startup.Token);
            }
            catch
            {
                try
                {
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(8));
                    await candidate.StopAsync(timeout.Token);
                }
                catch (Exception ex)
                {
                    DuckyLog.Log("Discord bot startup cleanup failed: " + ex);
                }
                finally
                {
                    gateway = null;
                    host = null;
                    try
                    {
                        candidate.Dispose();
                    }
                    catch (Exception ex)
                    {
                        DuckyLog.Log("Discord bot host disposal failed: " + ex);
                    }
                    ReleaseBotLease();
                }
                throw;
            }
        }
        finally
        {
            startupCancellation = null;
            lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? pendingStartup = startupCancellation;
        if (pendingStartup is not null)
        {
            try
            {
                await pendingStartup.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        await lifecycleLock.WaitAsync();
        try
        {
            IHost? currentHost = host;
            GatewayClient? currentGateway = gateway;
            gateway = null;
            host = null;

            Exception? stopFailure = null;
            try
            {
                if (currentHost is not null)
                {
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(8));
                    await currentHost.StopAsync(timeout.Token);
                }
                else if (currentGateway is not null &&
                         currentGateway.Status == WebSocketStatus.Ready)
                {
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(8));
                    await currentGateway.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Ducky Discord integration stopped",
                        timeout.Token);
                }
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains(
                    "Connection not started",
                    StringComparison.OrdinalIgnoreCase))
            {
                DuckyLog.Log(
                    "Discord gateway was already disconnected during shutdown.");
            }
            catch (Exception ex)
            {
                DuckyLog.Log("Discord bot shutdown failed: " + ex);
                stopFailure = ex;
            }
            finally
            {
                try
                {
                    currentHost?.Dispose();
                }
                catch (Exception ex)
                {
                    DuckyLog.Log("Discord bot host disposal failed: " + ex);
                    stopFailure ??= ex;
                }

                if (currentHost is null)
                {
                    try
                    {
                        currentGateway?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        DuckyLog.Log("Discord gateway disposal failed: " + ex);
                        stopFailure ??= ex;
                    }
                }

                ReleaseBotLease();
            }

            if (stopFailure is not null)
                throw new InvalidOperationException(
                    "Discord bot shutdown did not complete cleanly.",
                    stopFailure);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private void AcquireBotLease(string token)
    {
        string tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        string name = "Local\\DuckyDiscordBot-" + tokenHash;
        Semaphore lease = new(1, 1, name);
        if (!lease.WaitOne(0))
        {
            lease.Dispose();
            throw new InvalidOperationException(
                "Another Ducky instance is already connected with this Discord bot token. Close that instance first.");
        }

        botLease = lease;
    }

    private void ReleaseBotLease()
    {
        Semaphore? lease = botLease;
        botLease = null;
        if (lease is null)
            return;

        try
        {
            lease.Release();
        }
        finally
        {
            lease.Dispose();
        }
    }

}
