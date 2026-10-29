using System;
using System.IO;
using System.Threading;
using Avalonia.Threading;

namespace test;

internal sealed class ConfigHotReloader : IDisposable
{
    private readonly string configPath;
    private readonly FileSystemWatcher watcher;
    private readonly Timer debounceTimer;
    private Config currentConfig;
    private volatile bool disposed;

    public event Action<Config>? ConfigChanged;

    public ConfigHotReloader(Config initialConfig)
    {
        currentConfig = initialConfig;
        configPath = DuckyPaths.ConfigFilePath;
        debounceTimer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        watcher = new FileSystemWatcher(
            Path.GetDirectoryName(configPath)!,
            Path.GetFileName(configPath))
        {
            NotifyFilter = NotifyFilters.LastWrite |
                           NotifyFilters.Size |
                           NotifyFilters.FileName |
                           NotifyFilters.CreationTime
        };

        watcher.Changed += OnConfigFileChanged;
        watcher.Created += OnConfigFileChanged;
        watcher.Renamed += OnConfigFileChanged;
        watcher.Error += (_, e) =>
            DuckyLog.Log("Config file watcher error: " + e.GetException().Message);
        watcher.EnableRaisingEvents = true;
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        if (disposed)
            return;

        debounceTimer.Change(300, Timeout.Infinite);
    }

    private void Reload()
    {
        try
        {
            if (disposed || !File.Exists(configPath))
                return;

            Config nextConfig = ConfigReder.GetConfig(configPath);

            // Existing ConPTY sessions keep their original shell configuration.
            nextConfig.Shell = currentConfig.Shell;
            nextConfig.ShellPath = currentConfig.ShellPath;
            nextConfig.ShellArgs = currentConfig.ShellArgs;
            currentConfig = nextConfig;

            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed)
                    ConfigChanged?.Invoke(nextConfig);
            });
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Could not hot reload config: " + ex);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        watcher.Dispose();
        debounceTimer.Dispose();
    }
}
