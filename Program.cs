using Avalonia;
using System;

namespace ducky;

class Program
{
    public static bool StartInManagerMode { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        StartInManagerMode = Array.Exists(
            args,
            argument => string.Equals(
                argument,
                "--manager",
                StringComparison.OrdinalIgnoreCase))
                || ManagerSettingsStore.Load().OpenManagerOnStartup;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
