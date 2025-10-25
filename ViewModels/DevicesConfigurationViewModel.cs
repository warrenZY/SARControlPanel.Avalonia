using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.IO;
using System.Text.Json;
using NLog;
using SARControlPanel.Avalonia.Services;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace SARControlPanel.Avalonia.ViewModels;

public class SerialPortConfig
{
    public string? PortName { get; set; }
    public int BaudRate { get; set; }
    public int DataBits { get; set; }
    public StopBits StopBits { get; set; }
    public Parity Parity { get; set; }
}

public class SerialPortProfiles
{
    public Dictionary<string, SerialPortConfig> Profiles { get; set; } = new();
    public string DefaultProfileName { get; set; } = "Default";
}

public enum SerialConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public class DevicesConfigurationViewModel : ViewModelBase, IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private const string ConfigFileName = "SerialPortConfig.json";
    private const string ConfigMutexName = "Global\\SARControlPanel.SerialPortConfig.v1";

    private IDisposable? _fileWatcherSubscription;
    private readonly object _reloadLock = new();
    private SerialPort? _serialPort;
    private Dictionary<string, SerialPortConfig> _allProfiles = new();
    private bool _isDeleting = false;

    // Profile management properties
    private string _configName = "Default";
    public string ConfigName
    {
        get => _configName;
        set => this.RaiseAndSetIfChanged(ref _configName, value);
    }

    private ObservableCollection<string> _availableProfiles = new();
    public ObservableCollection<string> AvailableProfiles
    {
        get => _availableProfiles;
        set => this.RaiseAndSetIfChanged(ref _availableProfiles, value);
    }

    private string _selectedProfileName = "Default";
    public string SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (_selectedProfileName == value) return;
            this.RaiseAndSetIfChanged(ref _selectedProfileName, value);

            // Load profile settings when selecting an existing profile
            if (!string.IsNullOrWhiteSpace(value) && _allProfiles.ContainsKey(value))
            {
                LoadProfileSettings(value);
                ConfigName = value;
            }
            else if (!string.IsNullOrWhiteSpace(value))
            {
                ConfigName = value;
            }
        }
    }

    private string _defaultProfileName = "Default";
    public string DefaultProfileName
    {
        get => _defaultProfileName;
        set => this.RaiseAndSetIfChanged(ref _defaultProfileName, value);
    }

    private ObservableCollection<string> _availablePorts = new();
    public ObservableCollection<string> AvailablePorts
    {
        get => _availablePorts;
        set => this.RaiseAndSetIfChanged(ref _availablePorts, value);
    }

    private string? _selectedPort;
    public string? SelectedPort
    {
        get => _selectedPort;
        set => this.RaiseAndSetIfChanged(ref _selectedPort, value);
    }

    // Serial port configuration options
    public ObservableCollection<int> BaudRates { get; } = new ObservableCollection<int>
    {
        9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600
    };

    private int _selectedBaudRate = 115200;
    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => this.RaiseAndSetIfChanged(ref _selectedBaudRate, value);
    }

    public ObservableCollection<int> DataBitsOptions { get; } = new ObservableCollection<int> { 5, 6, 7, 8 };

    private int _selectedDataBits = 8;
    public int SelectedDataBits
    {
        get => _selectedDataBits;
        set => this.RaiseAndSetIfChanged(ref _selectedDataBits, value);
    }

    public ObservableCollection<StopBits> StopBitsOptions { get; } = new ObservableCollection<StopBits>
    {
        StopBits.One, StopBits.OnePointFive, StopBits.Two
    };

    private StopBits _selectedStopBits = StopBits.One;
    public StopBits SelectedStopBits
    {
        get => _selectedStopBits;
        set => this.RaiseAndSetIfChanged(ref _selectedStopBits, value);
    }

    public ObservableCollection<Parity> ParityOptions { get; } = new ObservableCollection<Parity>
    {
        Parity.None, Parity.Odd, Parity.Even, Parity.Mark, Parity.Space
    };

    private Parity _selectedParity = Parity.None;
    public Parity SelectedParity
    {
        get => _selectedParity;
        set => this.RaiseAndSetIfChanged(ref _selectedParity, value);
    }

    // Connection state management
    private bool _isConnected = false;
    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    private SerialConnectionState _currentState = SerialConnectionState.Disconnected;
    public SerialConnectionState CurrentState
    {
        get => _currentState;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentState, value);
            IsConnected = value == SerialConnectionState.Connected;
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private int _maxBackups = 5;

    // UI Commands
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleConnectionCommand { get; }

    public DevicesConfigurationViewModel()
    {
        // Initialize commands
        RefreshCommand = ReactiveCommand.Create(() =>
        {
            LoadPorts();
            TriggerReloadFromDisk();
        });

        SaveProfileCommand = ReactiveCommand.Create(SaveProfile);

        DeleteProfileCommand = ReactiveCommand.Create(DeleteProfile,
            this.WhenAnyValue(x => x.SelectedProfileName)
                .Select(name => !string.IsNullOrEmpty(name) && name != "Default"));

        var canMark = this.WhenAnyValue(x => x.SelectedProfileName, x => x.ConfigName,
                         (sel, cfg) => !string.IsNullOrWhiteSpace(sel) || !string.IsNullOrWhiteSpace(cfg));
        MarkProfileCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var target = !string.IsNullOrWhiteSpace(ConfigName) ? ConfigName.Trim() : SelectedProfileName;
            if (string.IsNullOrWhiteSpace(target))
            {
                ErrorMessage = "Profile name required to mark default.";
                return;
            }

            var cfg = new SerialPortConfig
            {
                PortName = SelectedPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                StopBits = SelectedStopBits,
                Parity = SelectedParity
            };
            _allProfiles[target] = cfg;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!AvailableProfiles.Contains(target))
                {
                    AvailableProfiles.Add(target);
                    AvailableProfiles = new ObservableCollection<string>(AvailableProfiles.OrderBy(n => n));
                }
                SelectedProfileName = target;
                ConfigName = target;
            });

            DefaultProfileName = target;
            await Task.Run(() => SaveAllProfilesToDisk());
            NotificationService.Instance.AddMessage($"Profile '{target}' saved and marked as default.", NotificationLevel.Info);
            _logger.Info($"Profile '{target}' saved and marked as default.");
        }, canMark);

        ToggleConnectionCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (IsConnected) await DisconnectDevice();
            else await ConnectDevice();
        });

        ToggleConnectionCommand.ThrownExceptions.Subscribe(ex =>
        {
            ErrorMessage = ex.Message;
            CurrentState = SerialConnectionState.Error;
            _logger.Error(ex, "Connection failed.");
        });

        // Initialize component
        LoadAllProfiles();
        StartWatchingConfigFile();
        LoadPorts();
    }

    /// <summary>
    /// Gets the configuration file path - uses executable directory for all platforms for easier access
    /// </summary>
    private static string GetConfigFilePath()
    {
        // Use the executable directory for all platforms for easier access and portability
        var exeDirectory = AppContext.BaseDirectory;
        return Path.Combine(exeDirectory, ConfigFileName);
    }

    /// <summary>
    /// Starts monitoring the configuration file for external changes
    /// </summary>
    private void StartWatchingConfigFile()
    {
        try
        {
            var configPath = GetConfigFilePath();
            var directory = Path.GetDirectoryName(configPath);
            var fileName = Path.GetFileName(configPath);

            if (string.IsNullOrEmpty(directory)) return;

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            // Create observable streams for file system events
            var changedEvents = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                h => watcher.Changed += h, h => watcher.Changed -= h);
            var createdEvents = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                h => watcher.Created += h, h => watcher.Created -= h);
            var deletedEvents = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                h => watcher.Deleted += h, h => watcher.Deleted -= h);
            var renamedEvents = Observable.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
                h => watcher.Renamed += h, h => watcher.Renamed -= h);

            // Merge and debounce file system events to prevent multiple rapid reloads
            _fileWatcherSubscription = Observable.Merge<object>(
                    changedEvents, createdEvents, deletedEvents, renamedEvents
                )
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(_ =>
                {
                    try
                    {
                        ReloadProfilesFromDisk();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Error while reloading profiles from disk.");
                    }
                });

            _logger.Info($"Started watching config file: {configPath}");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to start FileSystemWatcher for config file.");
        }
    }

    /// <summary>
    /// Loads profiles from the configuration file with validation
    /// </summary>
    private SerialPortProfiles? LoadProfilesFromFile()
    {
        var configPath = GetConfigFilePath();
        if (!File.Exists(configPath)) return null;

        try
        {
            using var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var profiles = JsonSerializer.Deserialize<SerialPortProfiles>(fs);

            if (profiles != null)
            {
                // Filter out invalid configurations
                var validProfiles = profiles.Profiles
                    .Where(kv => ValidateConfig(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                profiles.Profiles = validProfiles;
            }

            return profiles;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load profiles from file.");
            return null;
        }
    }

    /// <summary>
    /// Validates serial port configuration parameters
    /// </summary>
    private bool ValidateConfig(SerialPortConfig config)
    {
        if (config == null) return false;

        // Validate baud rate is in supported list
        if (!BaudRates.Contains(config.BaudRate))
        {
            _logger.Warn($"Unsupported baud rate in config: {config.BaudRate}");
            return false;
        }

        // Validate data bits is in supported list
        if (!DataBitsOptions.Contains(config.DataBits))
        {
            _logger.Warn($"Unsupported data bits in config: {config.DataBits}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Ensures the default profile always exists in the profiles dictionary
    /// </summary>
    private void EnsureDefaultProfileExists()
    {
        if (!_allProfiles.ContainsKey("Default"))
        {
            _allProfiles["Default"] = CreateDefaultConfig();
        }
    }

    /// <summary>
    /// Creates a default serial port configuration
    /// </summary>
    private SerialPortConfig CreateDefaultConfig()
    {
        return new SerialPortConfig
        {
            PortName = null,
            BaudRate = 115200,
            DataBits = 8,
            StopBits = StopBits.One,
            Parity = Parity.None
        };
    }

    /// <summary>
    /// Creates a backup of the current configuration file and manages old backups
    /// </summary>
    private void CreateConfigBackup()
    {
        var configPath = GetConfigFilePath();
        if (!File.Exists(configPath)) return;

        try
        {
            var backupDir = Path.Combine(Path.GetDirectoryName(configPath)!, "Backups");
            Directory.CreateDirectory(backupDir);

            // Create new backup with timestamp
            var backupPath = Path.Combine(backupDir,
                $"{Path.GetFileNameWithoutExtension(configPath)}_backup_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(configPath)}");

            File.Copy(configPath, backupPath);
            _logger.Info($"Configuration backup created: {backupPath}");

            // Clean up old backups, keep only the latest 10
            CleanupOldBackups(backupDir);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to create configuration backup");
        }
    }

    /// <summary>
    /// Cleans up old backup files, keeping only the most recent ones
    /// </summary>
    private void CleanupOldBackups(string backupDir)
    {
        try
        {
            var backupFiles = Directory.GetFiles(backupDir, "SerialPortConfig_backup_*.json")
                .Select(file => new FileInfo(file))
                .OrderByDescending(fi => fi.CreationTime)
                .ToList();

            // Keep only the 10 most recent backups
            if (backupFiles.Count > _maxBackups)
            {
                foreach (var oldBackup in backupFiles.Skip(_maxBackups))
                {
                    try
                    {
                        oldBackup.Delete();
                        _logger.Info($"Deleted old backup: {oldBackup.Name}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"Failed to delete old backup: {oldBackup.Name}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to clean up old backups");
        }
    }

    /// <summary>
    /// Triggers a manual reload of profiles from disk
    /// </summary>
    private void TriggerReloadFromDisk()
    {
        try
        {
            ReloadProfilesFromDisk();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error during manual config reload");
        }
    }

    /// <summary>
    /// Reloads profiles from disk when external changes are detected
    /// </summary>
    private void ReloadProfilesFromDisk()
    {
        lock (_reloadLock)
        {
            if (_isDeleting) return;

            var profilesWrapper = LoadProfilesFromFile();
            if (profilesWrapper == null) return;

            bool changed = false;
            bool currentProfileRemoved = false;

            // Check if current profile was removed externally
            if (!string.IsNullOrEmpty(SelectedProfileName) &&
                !profilesWrapper.Profiles.ContainsKey(SelectedProfileName))
            {
                currentProfileRemoved = true;
                changed = true;
            }

            // Update profiles dictionary
            _allProfiles.Clear();
            foreach (var kv in profilesWrapper.Profiles)
            {
                _allProfiles[kv.Key] = kv.Value;
                changed = true;
            }

            // Ensure default profile always exists
            EnsureDefaultProfileExists();

            // Update default profile name
            string defaultProfile = profilesWrapper.DefaultProfileName;
            if (!string.IsNullOrEmpty(defaultProfile) && _allProfiles.ContainsKey(defaultProfile))
            {
                DefaultProfileName = defaultProfile;
            }
            else
            {
                DefaultProfileName = "Default";
            }

            if (changed)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    AvailableProfiles = new ObservableCollection<string>(_allProfiles.Keys.OrderBy(n => n));

                    // Handle profile selection if current profile was removed
                    if (currentProfileRemoved || !AvailableProfiles.Contains(SelectedProfileName))
                    {
                        string newSelection = DetermineFallbackProfile();
                        SelectedProfileName = newSelection;
                        ConfigName = newSelection;
                        LoadProfileSettings(newSelection);

                        if (currentProfileRemoved)
                        {
                            NotificationService.Instance.AddMessage(
                                $"Selected profile was removed externally. Switched to '{newSelection}'.",
                                NotificationLevel.Warning);
                            _logger.Info($"Selected profile was removed externally. Switched to profile: {newSelection}");
                        }
                    }
                });
            }
        }
    }

    /// <summary>
    /// Determines the appropriate fallback profile when current selection is unavailable
    /// </summary>
    private string DetermineFallbackProfile()
    {
        // Priority order: default profile -> "Default" -> first available -> "Default" as last resort
        if (_allProfiles.ContainsKey(DefaultProfileName))
            return DefaultProfileName;
        else if (_allProfiles.ContainsKey("Default"))
            return "Default";
        else if (AvailableProfiles.Count > 0)
            return AvailableProfiles.First();
        else
            return "Default"; // Should never happen due to EnsureDefaultProfileExists
    }

    /// <summary>
    /// Loads profile settings into the UI controls
    /// </summary>
    private void LoadProfileSettings(string profileName)
    {
        if (string.IsNullOrEmpty(profileName))
        {
            ErrorMessage = "Invalid profile name.";
            CurrentState = SerialConnectionState.Error;
            _logger.Error("Invalid profile name.");
            return;
        }

        try
        {
            if (_allProfiles.TryGetValue(profileName, out var cfg))
            {
                SelectedPort = cfg.PortName;
                SelectedBaudRate = cfg.BaudRate;
                SelectedDataBits = cfg.DataBits;
                SelectedStopBits = cfg.StopBits;
                SelectedParity = cfg.Parity;
                ConfigName = profileName;
                ErrorMessage = null; // Clear any previous errors
            }
            else
            {
                ErrorMessage = $"Profile '{profileName}' not found.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load profile settings: {ex.Message}";
            CurrentState = SerialConnectionState.Error;
            _logger.Error(ex, $"Error loading profile settings for '{profileName}'.");
        }
    }

    /// <summary>
    /// Loads all profiles from disk during initialization
    /// </summary>
    private void LoadAllProfiles()
    {
        var profilesWrapper = LoadProfilesFromFile();

        if (profilesWrapper != null && profilesWrapper.Profiles.Count > 0)
        {
            foreach (var kv in profilesWrapper.Profiles)
            {
                _allProfiles[kv.Key] = kv.Value;
            }

            AvailableProfiles = new ObservableCollection<string>(_allProfiles.Keys.OrderBy(n => n));

            // Set default and selected profiles
            if (!string.IsNullOrEmpty(profilesWrapper.DefaultProfileName) &&
                _allProfiles.ContainsKey(profilesWrapper.DefaultProfileName))
            {
                DefaultProfileName = profilesWrapper.DefaultProfileName;
                SelectedProfileName = DefaultProfileName;
            }
            else if (_allProfiles.ContainsKey("Default"))
            {
                SelectedProfileName = "Default";
                DefaultProfileName = "Default";
            }
            else
            {
                SelectedProfileName = AvailableProfiles.FirstOrDefault() ?? string.Empty;
            }

            _logger.Info("All configurations loaded successfully.");
            return;
        }

        _logger.Info("Configuration file not found or invalid. Creating default profile.");

        // Initialize with default profile
        EnsureDefaultProfileExists();
        AvailableProfiles = new ObservableCollection<string> { "Default" };
        SelectedProfileName = "Default";
        ConfigName = "Default";
        DefaultProfileName = "Default";
        LoadProfileSettings("Default");
    }

    /// <summary>
    /// Saves all profiles to disk using mutex for cross-process synchronization
    /// </summary>
    private void SaveAllProfilesToDisk()
    {
        var configPath = GetConfigFilePath();
        var tempFile = configPath + ".tmp";

        var configToSave = new SerialPortProfiles
        {
            Profiles = new Dictionary<string, SerialPortConfig>(_allProfiles),
            DefaultProfileName = DefaultProfileName
        };

        bool mutexTaken = false;
        Mutex? mutex = null;

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Use named mutex for cross-process synchronization
            mutex = new Mutex(false, ConfigMutexName);
            try
            {
                mutexTaken = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                mutexTaken = true;
            }

            if (!mutexTaken)
            {
                throw new TimeoutException("Could not acquire configuration mutex within timeout");
            }

            // Create backup only if the original file exists
            if (File.Exists(configPath))
            {
                CreateConfigBackup();
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
                // Remove PropertyNamingPolicy to maintain original PascalCase format
            };

            var jsonString = JsonSerializer.Serialize(configToSave, options);
            File.WriteAllText(tempFile, jsonString);

            // Verify the saved file can be read back correctly
            try
            {
                var verification = JsonSerializer.Deserialize<SerialPortProfiles>(File.ReadAllText(tempFile));
                if (verification == null)
                    throw new InvalidDataException("Failed to verify saved configuration");
            }
            catch
            {
                throw new InvalidDataException("Saved configuration file is corrupted");
            }

            // Atomic file replacement
            if (File.Exists(configPath))
            {
                File.Replace(tempFile, configPath, null);
            }
            else
            {
                File.Move(tempFile, configPath);
            }

            _logger.Info($"Configuration saved successfully. Profile count: {_allProfiles.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configurations to disk.");
            throw;
        }
        finally
        {
            if (mutexTaken && mutex != null)
            {
                try { mutex.ReleaseMutex(); } catch { /* Ignore cleanup errors */ }
            }
            mutex?.Dispose();

            // Clean up temporary file
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* Ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Deletes the currently selected profile
    /// </summary>
    public void DeleteProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileName))
        {
            ErrorMessage = "No profile selected.";
            CurrentState = SerialConnectionState.Error;
            _logger.Error("No profile selected.");
            return;
        }

        if (SelectedProfileName == "Default")
        {
            ErrorMessage = "Cannot delete the Default profile.";
            _logger.Warn("Attempted to delete Default profile.");
            return;
        }

        try
        {
            _isDeleting = true;
            string profileToDelete = SelectedProfileName;
            bool wasDefault = (profileToDelete == DefaultProfileName);

            if (_allProfiles.Remove(profileToDelete))
            {
                if (wasDefault)
                {
                    DefaultProfileName = "Default";
                }

                AvailableProfiles = new ObservableCollection<string>(_allProfiles.Keys.OrderBy(n => n));

                string newSelection = AvailableProfiles.Contains(DefaultProfileName)
                    ? DefaultProfileName
                    : (AvailableProfiles.Count > 0 ? AvailableProfiles.First() : "Default");

                SelectedProfileName = newSelection;
                ConfigName = newSelection;
                LoadProfileSettings(newSelection);

                SaveAllProfilesToDisk();
                NotificationService.Instance.AddMessage($"Profile '{profileToDelete}' deleted. Switched to '{newSelection}'.", NotificationLevel.Info);
                _logger.Info($"Profile '{profileToDelete}' deleted. Switched to profile: {newSelection}");
            }
            else
            {
                ErrorMessage = $"Profile '{profileToDelete}' not found.";
                _logger.Warn($"Failed to delete profile '{profileToDelete}' - not found in dictionary.");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete profile: {ex.Message}";
            _logger.Error(ex, $"Error deleting profile '{SelectedProfileName}'.");
        }
        finally
        {
            _isDeleting = false;
        }
    }

    /// <summary>
    /// Saves the current settings as a profile
    /// </summary>
    public void SaveProfile()
    {
        // Debug logging to understand the issue
        _logger.Debug($"SaveProfile called - ConfigName: '{ConfigName}', SelectedProfileName: '{SelectedProfileName}'");

        // Use ConfigName if provided and not empty, otherwise use SelectedProfileName
        var profileName = !string.IsNullOrWhiteSpace(ConfigName) ? ConfigName.Trim() :
                         !string.IsNullOrWhiteSpace(SelectedProfileName) ? SelectedProfileName : null;

        if (string.IsNullOrWhiteSpace(profileName))
        {
            ErrorMessage = "Profile name cannot be empty.";
            _logger.Warn("SaveProfile failed: Profile name is empty");
            return;
        }

        _logger.Debug($"Using profile name: '{profileName}' for saving");

        if (profileName == "Default" && !_allProfiles.ContainsKey("Default"))
        {
            ErrorMessage = "Cannot modify the built-in Default profile.";
            return;
        }

        try
        {
            var config = new SerialPortConfig
            {
                PortName = SelectedPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                StopBits = SelectedStopBits,
                Parity = SelectedParity
            };

            _allProfiles[profileName] = config;

            Dispatcher.UIThread.Post(() =>
            {
                if (!AvailableProfiles.Contains(profileName))
                {
                    AvailableProfiles.Add(profileName);
                    AvailableProfiles = new ObservableCollection<string>(AvailableProfiles.OrderBy(n => n));
                }

                SelectedProfileName = profileName;
                ConfigName = profileName;
            });

            SaveAllProfilesToDisk();
            ErrorMessage = null; // Clear any previous errors
            NotificationService.Instance.AddMessage($"Profile '{profileName}' saved successfully.", NotificationLevel.Info);
            _logger.Info($"Profile '{profileName}' saved successfully.");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save profile: {ex.Message}";
            _logger.Error(ex, $"Failed to save profile '{profileName}'");
        }
    }

    /// <summary>
    /// Refreshes the list of available serial ports
    /// </summary>
    public void LoadPorts()
    {
        try
        {
            string[] ports = SerialPort.GetPortNames();
            var ordered = ports.OrderBy(p => p).ToArray();

            if (!AvailablePortsSequenceEqual(ordered))
            {
                string? savedPort = _selectedPort;

                AvailablePorts = new ObservableCollection<string>(ordered);

                if (!string.IsNullOrEmpty(savedPort) && AvailablePorts.Contains(savedPort))
                {
                    SelectedPort = savedPort;
                    _logger.Info($"Restored saved port: {SelectedPort}");
                }
                else if (AvailablePorts.Count > 0)
                {
                    // Select port based on current profile or first available
                    SelectedPort = AvailableProfiles.Contains(SelectedProfileName) && _allProfiles.ContainsKey(SelectedProfileName)
                        ? (_allProfiles[SelectedProfileName].PortName ?? AvailablePorts.First())
                        : AvailablePorts.First();

                    _logger.Info($"Selected port: {SelectedPort}");
                }
                else
                {
                    SelectedPort = null;
                    _logger.Info("No serial ports found.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enumerate serial ports.");
        }
    }

    /// <summary>
    /// Compares current available ports with new port list to detect changes
    /// </summary>
    private bool AvailablePortsSequenceEqual(string[] ordered)
    {
        if (_availablePorts == null) return ordered.Length == 0;
        if (_availablePorts.Count != ordered.Length) return false;
        for (int i = 0; i < ordered.Length; i++)
            if (_availablePorts[i] != ordered[i]) return false;
        return true;
    }

    /// <summary>
    /// Establishes connection to the selected serial port
    /// </summary>
    private async Task ConnectDevice()
    {
        if (string.IsNullOrEmpty(SelectedPort))
        {
            ErrorMessage = "Invalid port";
            CurrentState = SerialConnectionState.Error;
            _logger.Error("Connection attempt failed: No port selected.");
            return;
        }

        CurrentState = SerialConnectionState.Connecting;
        ErrorMessage = null;
        NotificationService.Instance.AddMessage($"Connecting to {SelectedPort} at {SelectedBaudRate} baud...", NotificationLevel.Info);
        _logger.Info($"Connecting to {SelectedPort} at {SelectedBaudRate} baud...");

        try
        {
            _serialPort?.Dispose();
            _serialPort = new SerialPort(SelectedPort, SelectedBaudRate);

            _serialPort.DataBits = SelectedDataBits;
            _serialPort.Parity = SelectedParity;
            _serialPort.StopBits = SelectedStopBits;

            _serialPort.Open();

            // Register the opened port with shared SerialPortService
            SerialPortService.Instance.RegisterSerialPort(_serialPort);

            CurrentState = SerialConnectionState.Connected;
            NotificationService.Instance.AddMessage($"Successfully connected to {SelectedPort}.", NotificationLevel.Info);
            _logger.Info($"Successfully connected to {SelectedPort}.");

            // Save current settings as they resulted in successful connection
            SaveProfile();
        }
        catch (Exception ex)
        {
            NotificationService.Instance.AddMessage($"Failed to connect: {ex.Message}", NotificationLevel.Error);
            CurrentState = SerialConnectionState.Error;
            _logger.Error(ex, "Connection failed");

            // Ensure resources are cleaned up on connection failure
            try
            {
                SerialPortService.Instance.RegisterSerialPort(null);
                _serialPort?.Close();
                _serialPort?.Dispose();
                _serialPort = null;
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Disconnects from the serial port
    /// </summary>
    private async Task DisconnectDevice()
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            _logger.Info($"Attempting to disconnect from {SelectedPort}...");

            try
            {
                // Unregister from shared service before disposing the port
                SerialPortService.Instance.RegisterSerialPort(null);

                _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;

                CurrentState = SerialConnectionState.Disconnected;
                ErrorMessage = null;
                NotificationService.Instance.AddMessage("Successfully disconnected.", NotificationLevel.Info);
                _logger.Info("Successfully disconnected.");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during disconnect: {ex.Message}";
                CurrentState = SerialConnectionState.Error;
                _logger.Error(ex, "Error during disconnect attempt.");
            }
        }
        else
        {
            CurrentState = SerialConnectionState.Disconnected;
            ErrorMessage = null;
        }
    }

    /// <summary>
    /// Cleans up resources
    /// </summary>
    public void Dispose()
    {
        _fileWatcherSubscription?.Dispose();
        _serialPort?.Dispose();
    }
}