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
    // renamed per request: default profile name (explicitly set by user)
    public string DefaultProfileName { get; set; } = "Default";
}

public enum SerialConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public class DevicesConfigurationViewModel : ViewModelBase
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private const string ConfigFileName = "SerialPortConfig.json";

    // Named mutex used to serialize cross-process config writes.
    private const string ConfigMutexName = "SARControlPanel.SerialPortConfig.Mutex.v1";

    // File watcher + debounce timer to pick up external changes
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private readonly object _reloadLock = new();

    private SerialPort? _serialPort;
    private Dictionary<string, SerialPortConfig> _allProfiles = new();

    // --- Profile Management Properties ---
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

            // Keep the Save As box in-sync with the chosen profile.
            // If the selected name corresponds to an existing profile, load its settings.
            if (!string.IsNullOrWhiteSpace(value) && _allProfiles.ContainsKey(value))
            {
                // Load profile settings and reflect name into ConfigName
                LoadProfileSettings(value);
                ConfigName = value;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(value))
                    ConfigName = value;
            }
        }
    }


    private string _defaultProfileName = "Default";
    /// <summary>
    /// Name of the profile marked as default by the user.
    /// </summary>
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

    // Commands
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleConnectionCommand { get; }

    public DevicesConfigurationViewModel()
    {
        // Commands
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
            // prefer typed ConfigName, else selected
            var target = !string.IsNullOrWhiteSpace(ConfigName) ? ConfigName.Trim() : SelectedProfileName;
            if (string.IsNullOrWhiteSpace(target))
            {
                ErrorMessage = "Profile name required to mark default.";
                return;
            }

            // save into memory
            var cfg = new SerialPortConfig
            {
                PortName = SelectedPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                StopBits = SelectedStopBits,
                Parity = SelectedParity
            };
            _allProfiles[target] = cfg;

            // update UI
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
            // persist merged file
            await Task.Run(() => SaveAllProfilesToDisk());
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
            NotificationService.Instance.AddMessage($"Connection failed: {ex.Message}", NotificationLevel.Error);
            CurrentState = SerialConnectionState.Error;
            _logger.Error(ex, "Connection failed.");
        });

        // initialize
        LoadAllProfiles();
        StartWatchingConfigFile();
        LoadPorts();
    }

    private void StartWatchingConfigFile()
    {
        try
        {
            // watch current directory for changes to the config file
            _watcher = new FileSystemWatcher(Directory.GetCurrentDirectory(), ConfigFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };

            FileSystemEventHandler handler = (s, e) => TriggerReloadFromDisk();
            RenamedEventHandler renHandler = (s, e) => TriggerReloadFromDisk();

            _watcher.Changed += handler;
            _watcher.Created += handler;
            _watcher.Deleted += handler;
            _watcher.Renamed += renHandler;
            _watcher.EnableRaisingEvents = true;

            // debounce timer (single timer instance): 500ms debounce
            _reloadTimer = new Timer(_ =>
            {
                try
                {
                    ReloadProfilesFromDisk();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Error while reloading profiles from disk.");
                }
            }, null, Timeout.Infinite, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to start FileSystemWatcher for config file.");
        }
    }

    private void TriggerReloadFromDisk()
    {
        try { _reloadTimer?.Change(500, Timeout.Infinite); } catch { }
    }

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
                    SelectedPort = AvailableProfiles.Contains(SelectedProfileName) && _allProfiles.ContainsKey(SelectedProfileName)
                        ? (_allProfiles[SelectedProfileName].PortName ?? AvailableProfiles.First())
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

    private bool AvailablePortsSequenceEqual(string[] ordered)
    {
        if (_availablePorts == null) return ordered.Length == 0;
        if (_availablePorts.Count != ordered.Length) return false;
        for (int i = 0; i < ordered.Length; i++)
            if (_availablePorts[i] != ordered[i]) return false;
        return true;
    }

    private bool _isDeleting = false;
    private string? _recentlyDeletedProfile = null;
    private readonly object _deleteLock = new();

    private void ReloadProfilesFromDisk()
    {
        lock (_reloadLock)
        {
            if (_isDeleting)
            {
                return;
            }

            if (!File.Exists(ConfigFileName)) return;

            try
            {
                using var fs = new FileStream(ConfigFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var profilesWrapper = JsonSerializer.Deserialize<SerialPortProfiles>(fs);
                if (profilesWrapper == null) return;

                bool changed = false;
                bool currentProfileRemoved = false;

                // Check if current profile still exists in the loaded profiles
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
                    if (kv.Key != _recentlyDeletedProfile)
                    {
                        _allProfiles[kv.Key] = kv.Value;
                        changed = true;
                    }
                }

                // Update default profile name if it exists in loaded profiles
                string defaultProfile = profilesWrapper.DefaultProfileName;
                if (!string.IsNullOrEmpty(defaultProfile) && _allProfiles.ContainsKey(defaultProfile))
                {
                    DefaultProfileName = defaultProfile;
                }
                // If default profile doesn't exist or was removed, ensure "Default" exists
                else if (!_allProfiles.ContainsKey("Default"))
                {
                    _allProfiles["Default"] = new SerialPortConfig
                    {
                        PortName = null,
                        BaudRate = 115200,
                        DataBits = 8,
                        StopBits = StopBits.One,
                        Parity = Parity.None
                    };
                    DefaultProfileName = "Default";
                    changed = true;
                }

                if (changed)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        // Update available profiles
                        AvailableProfiles = new ObservableCollection<string>(_allProfiles.Keys.OrderBy(n => n));

                        // Handle profile selection
                        if (currentProfileRemoved || !AvailableProfiles.Contains(SelectedProfileName))
                        {
                            string newSelection;
                            // First try to use the configured default profile
                            if (_allProfiles.ContainsKey(DefaultProfileName))
                            {
                                newSelection = DefaultProfileName;
                            }
                            // Fall back to "Default" if it exists
                            else if (_allProfiles.ContainsKey("Default"))
                            {
                                newSelection = "Default";
                                DefaultProfileName = "Default";
                            }
                            // Last resort: use first available profile
                            else if (AvailableProfiles.Count > 0)
                            {
                                newSelection = AvailableProfiles.First();
                            }
                            else
                            {
                                // This should never happen as we ensure "Default" always exists
                                newSelection = "Default";
                            }

                            // Update selection and load settings
                            SelectedProfileName = newSelection;
                            ConfigName = newSelection;
                            LoadProfileSettings(newSelection);

                            if (currentProfileRemoved)
                            {
                                _logger.Info($"Selected profile was removed externally. Switched to profile: {newSelection}");
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to reload config file during filesystem change handling.");
            }
        }
    }


    /// <summary>
    /// Load a profile settings into the viewmodel fields (port, baud, data bits, stop bits, parity).
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
    /// Load all profiles from disk into _allProfiles and update AvailableProfiles/default selection.
    /// </summary>
    private void LoadAllProfiles()
    {
        if (File.Exists(ConfigFileName))
        {
            try
            {
                using var fs = new FileStream(ConfigFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var profilesWrapper = JsonSerializer.Deserialize<SerialPortProfiles>(fs);

                if (profilesWrapper != null && profilesWrapper.Profiles.Count > 0)
                {
                    foreach (var kv in profilesWrapper.Profiles)
                    {
                        _allProfiles[kv.Key] = kv.Value;
                    }

                    AvailableProfiles = new ObservableCollection<string>(_allProfiles.Keys.OrderBy(n => n));

                    if (!string.IsNullOrEmpty(profilesWrapper.DefaultProfileName) && _allProfiles.ContainsKey(profilesWrapper.DefaultProfileName))
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
                _logger.Info("Configuration file found but was empty or invalid.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load or deserialize configuration file. Creating default profile.");
            }
        }
        else
        {
            _logger.Info("Configuration file not found. Creating default profile.");
        }

        // initialize default profile
        _allProfiles["Default"] = new SerialPortConfig
        {
            PortName = null,
            BaudRate = 115200,
            DataBits = 8,
            StopBits = StopBits.One,
            Parity = Parity.None
        };

        AvailableProfiles = new ObservableCollection<string> { "Default" };
        SelectedProfileName = "Default";
        ConfigName = "Default";
        DefaultProfileName = "Default";
        LoadProfileSettings("Default");
    }

    /// <summary>
    /// Merge local profiles into disk file using named mutex to avoid clobber by other instances.
    /// </summary>
    private void SaveAllProfilesToDisk()
    {
        string tempFile = ConfigFileName + ".tmp";
        var configToSave = new SerialPortProfiles
        {
            Profiles = new Dictionary<string, SerialPortConfig>(_allProfiles),
            DefaultProfileName = DefaultProfileName
        };

        bool mutexTaken = false;
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, ConfigMutexName);
            try
            {
                mutexTaken = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                mutexTaken = true;
            }

            // Serialize and save current configuration directly
            var options = new JsonSerializerOptions { WriteIndented = true };
            var jsonString = JsonSerializer.Serialize(configToSave, options);

            // Write configuration using temp file
            File.WriteAllText(tempFile, jsonString);

            // Atomic file replacement
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    File.Replace(tempFile, ConfigFileName, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(ConfigFileName);
                    File.Move(tempFile, ConfigFileName);
                }
            }
            else
            {
                File.Move(tempFile, ConfigFileName);
            }

            _logger.Info($"Configuration saved successfully. Profile count: {_allProfiles.Count}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configurations to disk.");
            throw; // Propagate error to caller
        }
        finally
        {
            if (mutexTaken && mutex != null)
            {
                try { mutex.ReleaseMutex(); } catch { /* ignore */ }
            }
            mutex?.Dispose();
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* ignore */ }
        }
    }

    // DeleteProfile adjusted to update UI selection reliably
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
            string profileToDelete = SelectedProfileName;
            bool wasDefault = (profileToDelete == DefaultProfileName);

            if (_allProfiles.Remove(profileToDelete))
            {
                if (wasDefault)
                {
                    DefaultProfileName = "Default";
                }

                AvailableProfiles = new ObservableCollection<string>(_allProfiles.Keys.OrderBy(n => n));

                string newSelection = AvailableProfiles.Contains(DefaultProfileName) ? DefaultProfileName :
                                    (AvailableProfiles.Count > 0 ? AvailableProfiles.First() : "Default");

                SelectedProfileName = newSelection;
                ConfigName = newSelection;
                LoadProfileSettings(newSelection);

                SaveAllProfilesToDisk();
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
    }

    /// <summary>
    /// Persist current UI settings into the profile named ConfigName (Save button).
    /// </summary>
    public void SaveProfile()
    {
        if (string.IsNullOrEmpty(ConfigName))
        {
            ErrorMessage = "Profile name cannot be empty.";
            NotificationService.Instance.AddMessage("Profile name cannot be empty.", NotificationLevel.Warning);
            return;
        }

        var profileName = ConfigName.Trim();
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
            ErrorMessage = $"Profile '{profileName}' saved successfully.";
            NotificationService.Instance.AddMessage($"Profile '{profileName}' saved successfully.", NotificationLevel.Info);
            _logger.Info($"Profile '{profileName}' saved successfully.");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save profile: {ex.Message}";
            NotificationService.Instance.AddMessage($"Failed to save profile: {ex.Message}", NotificationLevel.Error);
            _logger.Info(ex, "Failed to save profile");
        }
    }

    /// <summary>
    /// Connects to the device and applies all serial port settings.
    /// </summary>
    private async Task ConnectDevice()
    {
        if (string.IsNullOrEmpty(SelectedPort))
        {
            ErrorMessage = "Invalid port";
            NotificationService.Instance.AddMessage("No port selected. Cannot connect.", NotificationLevel.Error);
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
            SaveProfile(); // Persist the successful connection settings
        }
        finally
        {
            if (!IsConnected)
            {
                try
                {
                    // Ensure the shared service does not keep a reference to a failed port
                    SerialPortService.Instance.RegisterSerialPort(null);
                }
                catch { /* ignore */ }

                _serialPort?.Close();
                _serialPort?.Dispose();
                _serialPort = null;
            }
        }
    }

    /// <summary>
    /// Disconnects the device.
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
}
