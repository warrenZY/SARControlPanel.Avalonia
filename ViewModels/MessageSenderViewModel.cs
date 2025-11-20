using ReactiveUI;
using SARControlPanel.Avalonia.Services;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using NLog;

namespace SARControlPanel.Avalonia.ViewModels;

public class MessageSenderViewModel : ViewModelBase, IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly ISerialPortService _serialService = null!;
    
    public MessagingStateViewModel State { get; } = null!;
    
    private string _dataToSend = string.Empty;
    public string DataToSend
    {
        get => _dataToSend;
        set => this.RaiseAndSetIfChanged(ref _dataToSend, value);
    }

    private string? _sendErrorMessage;
    public string? SendErrorMessage
    {
        get => _sendErrorMessage;
        private set => this.RaiseAndSetIfChanged(ref _sendErrorMessage, value);
    }

    private bool _hasSendError;
    public bool HasSendError
    {
        get => _hasSendError;
        private set => this.RaiseAndSetIfChanged(ref _hasSendError, value);
    }

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    private IDisposable? _commandExceptionSubscription;

    public MessageSenderViewModel()
        : this(SerialPortService.Null, new MessagingStateViewModel())
    {
    }

    /// <summary>
    /// Initializes the view model with a serial service and shared messaging state.
    /// </summary>
    /// <param name="serialService">The serial port service for sending data.</param>
    /// <param name="state">The shared messaging state containing HEX mode and counters.</param>
    public MessageSenderViewModel(ISerialPortService serialService, MessagingStateViewModel state)
    {
        _serialService = serialService ?? SerialPortService.Null;
        State = state ?? new MessagingStateViewModel();

        var canSend = this.WhenAnyValue(vm => vm.DataToSend)
                          .Select(text => !string.IsNullOrWhiteSpace(text));

        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);

        SubscribeToCommandExceptions();

        this.WhenAnyValue(vm => vm.DataToSend)
            .Subscribe(_ =>
            {
                SendErrorMessage = null;
                HasSendError = false;
            });
    }

    private void SubscribeToCommandExceptions()
    {
        try
        {
            _commandExceptionSubscription = SendCommand.ThrownExceptions.Subscribe(ex =>
            {
                _logger.Error(ex, "SendCommand error");
                try
                {
                    UpdateSendError($"Send failed: {ex.Message}");
                    NotificationService.Instance.AddMessage($"Send error: {ex.Message}", NotificationLevel.Error);
                }
                catch (Exception inner)
                {
                    _logger.Warn(inner, "Failed while handling SendCommand exception.");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to subscribe to command exceptions.");
        }
    }

    /// <summary>
    /// Sends data to the serial port with HEX/ASCII conversion and message history recording.
    /// </summary>
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(DataToSend))
            return;

        byte[] payload;
        if (State.IsHexMode)
        {
            if (!TryParseHexString(DataToSend, out payload))
            {
                UpdateSendError("Invalid HEX format. Use pairs like: 0A 1B FF or 0A1BFF.");
                return;
            }
        }
        else
        {
            payload = Encoding.ASCII.GetBytes(DataToSend);
        }

        int sent = await _serialService.SendDataAsync(payload).ConfigureAwait(false);

        Dispatcher.UIThread.Post(() =>
        {
            State.BytesSentCount += sent;
            SendErrorMessage = null;
            HasSendError = false;

            string formatted;
            if (State.IsHexMode)
                formatted = string.Concat(payload.Select(b => b.ToString("X2") + " "));
            else
                formatted = Encoding.ASCII.GetString(payload);

            State.SentMessages.Add($"TX: {formatted.TrimEnd()}");
        });
    }

    private void UpdateSendError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SendErrorMessage = message;
            HasSendError = true;
        });
    }

    /// <summary>
    /// Parses a HEX string into bytes. Accepts space-separated or contiguous pairs.
    /// </summary>
    /// <param name="input">The HEX string to parse (e.g., "0A 1B FF" or "0A1BFF").</param>
    /// <param name="bytes">Output parameter containing the parsed byte array, or empty array on failure.</param>
    /// <returns>True if parsing succeeded; false if input is invalid or malformed.</returns>
    private static bool TryParseHexString(string input, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(input)) return false;
        string filtered = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (filtered.Length % 2 != 0) return false;

        try
        {
            bytes = Enumerable.Range(0, filtered.Length / 2)
                              .Select(i => Convert.ToByte(filtered.Substring(i * 2, 2), 16))
                              .ToArray();
            return true;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    public void Dispose()
    {
        _commandExceptionSubscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}