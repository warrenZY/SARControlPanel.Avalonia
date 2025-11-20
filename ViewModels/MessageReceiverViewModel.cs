using ReactiveUI;
using SARControlPanel.Avalonia.Services;
using System;
using System.Linq;
using System.Reactive;
using System.Text;
using Avalonia.Threading;
using System.Collections.Specialized;
using NLog;

namespace SARControlPanel.Avalonia.ViewModels;

public class MessageReceiverViewModel : ViewModelBase, IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
    private readonly ISerialPortService _serialPortService = null!;
    
    public MessagingStateViewModel State { get; } = null!;

    private string _receivedDataText = string.Empty;
    public string ReceivedDataText
    {
        get => _receivedDataText;
        private set => this.RaiseAndSetIfChanged(ref _receivedDataText, value);
    }

    private bool _autoScrollEnabled = true;
    public bool AutoScrollEnabled
    {
        get => _autoScrollEnabled;
        set => this.RaiseAndSetIfChanged(ref _autoScrollEnabled, value);
    }

    public ReactiveCommand<Unit, Unit> ClearReceivedDataCommand { get; }

    private NotifyCollectionChangedEventHandler? _sentMessagesHandler;
    private IDisposable? _commandExceptionSubscription;

    public MessageReceiverViewModel()
        : this(SerialPortService.Null, new MessagingStateViewModel())
    {
    }

    /// <summary>
    /// Initializes the view model with a serial service and shared messaging state.
    /// Subscribes to data reception and message history events.
    /// </summary>
    /// <param name="serialPortService">The serial port service for receiving data.</param>
    /// <param name="state">The shared messaging state containing HEX mode and counters.</param>
    public MessageReceiverViewModel(ISerialPortService serialPortService, MessagingStateViewModel state)
    {
        _serialPortService = serialPortService ?? SerialPortService.Null;
        State = state ?? new MessagingStateViewModel();

        _serialPortService.DataReceived += OnDataReceived;

        _sentMessagesHandler = new NotifyCollectionChangedEventHandler((s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is string text)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            ReceivedDataText += text + Environment.NewLine;
                        });
                    }
                }
            }
        });

        State.SentMessages.CollectionChanged += _sentMessagesHandler;

        ClearReceivedDataCommand = ReactiveCommand.Create(() =>
        {
            ReceivedDataText = string.Empty;
        });

        SubscribeToCommandExceptions();
    }

    private void SubscribeToCommandExceptions()
    {
        try
        {
            _commandExceptionSubscription = ClearReceivedDataCommand.ThrownExceptions.Subscribe(ex =>
            {
                _logger.Error(ex, "ClearReceivedDataCommand error");
                try
                {
                    NotificationService.Instance.AddMessage($"Clear error: {ex.Message}", NotificationLevel.Error);
                }
                catch (Exception inner)
                {
                    _logger.Warn(inner, "Failed while handling ClearReceivedDataCommand exception.");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to subscribe to command exceptions.");
        }
    }

    private void OnDataReceived(byte[] data)
    {
        if (data == null || data.Length == 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            State.BytesReceivedCount += data.Length;

            string formattedData;
            if (State.IsHexMode)
                formattedData = string.Concat(data.Select(b => b.ToString("X2") + " "));
            else
                formattedData = Encoding.ASCII.GetString(data);

            ReceivedDataText += $"RX: {formattedData}" + Environment.NewLine;
        });
    }

    public void Dispose()
    {
        _serialPortService.DataReceived -= OnDataReceived;

        if (_sentMessagesHandler != null)
            State.SentMessages.CollectionChanged -= _sentMessagesHandler;

        _commandExceptionSubscription?.Dispose();

        GC.SuppressFinalize(this);
    }
}
