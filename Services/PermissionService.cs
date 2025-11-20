using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SARControlPanel.Avalonia.Services
{
    public sealed class PermissionService
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private static readonly PermissionService _instance = new();
        public static PermissionService Instance => _instance;

        private PermissionService() { }

        public bool IsRunningAsAdmin()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;

            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the application has read/write permissions for a directory.
        /// </summary>
        /// <param name="directoryPath">The directory path to check.</param>
        /// <returns>True if directory is accessible and writable; false otherwise.</returns>
        public bool CheckDirectoryPermissions(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return false;

            try
            {
                _ = Directory.EnumerateFileSystemEntries(directoryPath).FirstOrDefault();
                
                string testFilePath = Path.Combine(directoryPath, $".test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFilePath, "test");
                File.Delete(testFilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the application has read/write permissions for a file.
        /// If the file doesn't exist, checks its parent directory.
        /// </summary>
        /// <param name="filePath">The file path to check.</param>
        /// <returns>True if file or parent directory is accessible; false otherwise.</returns>
        public bool CheckFilePermissions(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                if (!File.Exists(filePath))
                {
                    var dir = Path.GetDirectoryName(filePath);
                    return !string.IsNullOrEmpty(dir) && CheckDirectoryPermissions(dir);
                }

                using (var fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures the directory exists and has appropriate permissions.
        /// Creates the directory if it doesn't exist.
        /// </summary>
        /// <param name="directoryPath">The directory path to ensure.</param>
        /// <returns>True if directory is accessible; false otherwise.</returns>
        public bool EnsurePermissions(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                return CheckDirectoryPermissions(directoryPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Requests elevation to administrator privileges by restarting with "runas" verb.
        /// Returns false if already admin, cancelled, or on non-Windows platforms.
        /// </summary>
        /// <returns>True if elevation was requested; false otherwise.</returns>
        public bool RequestElevation()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IsRunningAsAdmin())
                return false;

            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    _logger.Warn("Failed to get executable path for elevation request");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        _logger.Info($"Elevation requested. New process started with PID: {process.Id}");
                        process.Dispose();
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.Info("User cancelled elevation request");
                    return false;
                }
                catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
                {
                    _logger.Info("User cancelled elevation request (error 1223)");
                    return false;
                }

                return false;
            }
            catch (System.ComponentModel.Win32Exception wex)
            {
                _logger.Warn(wex, $"Win32 error during elevation (code: {wex.NativeErrorCode})");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error during elevation request");
                return false;
            }
        }
    }
}
