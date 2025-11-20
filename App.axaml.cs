using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SARControlPanel.Avalonia.Services;
using SARControlPanel.Avalonia.ViewModels;
using SARControlPanel.Avalonia.Views;
using NLog;
using System;

namespace SARControlPanel.Avalonia
{
    public partial class App : Application
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            _logger.Info("=== Application Framework Initialization Completed ===");

            CheckPermissionsAndNotify();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
                
                _logger.Info("? Main window created and displayed");
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void CheckPermissionsAndNotify()
        {
            try
            {
                var configDir = AppContext.BaseDirectory;
                if (!PermissionService.Instance.CheckDirectoryPermissions(configDir))
                {
                    _logger.Warn("Application is running without write permissions to app directory");
                    NotificationService.Instance.AddMessage(
                        "Elevation denied, continuing without admin privileges. Configuration and log file access may be limited.",
                        NotificationLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error checking permissions after framework initialization");
            }
        }
    }
}