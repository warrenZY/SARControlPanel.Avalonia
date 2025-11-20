using System;
using System.Reactive;
using Avalonia;
using ReactiveUI;
using ReactiveUI.Avalonia;
using SARControlPanel.Avalonia.Services;
using NLog;

namespace SARControlPanel.Avalonia
{
    internal sealed class Program
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        [STAThread]
        public static void Main(string[] args)
        {
            // Initialization code. Don't use any Avalonia, third-party APIs or any
            // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
            // yet and stuff might break.
            RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
            {
                try
                {
                    _logger.Error(ex, "Unhandled ReactiveUI exception - this should have been caught at module level");
                }
                catch (Exception innerEx)
                {
                    _logger.Fatal(innerEx, "Failed to handle exception in RxApp.DefaultExceptionHandler");
                }
            });

            var configDir = AppContext.BaseDirectory;
            
            if (!PermissionService.Instance.CheckDirectoryPermissions(configDir))
            {
                _logger.Warn($"No write permission for {configDir}, requesting elevation...");
                if (PermissionService.Instance.RequestElevation())
                    return;
                _logger.Warn("Elevation denied, continuing without admin privileges");
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI();
    }
}
