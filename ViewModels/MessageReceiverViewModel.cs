using ReactiveUI;
using SARControlPanel.Avalonia.Services;
using System;
using System.Linq;
using System.Reactive;
using System.Text;
using Avalonia.Threading;
using System.Collections.Specialized;

namespace SARControlPanel.Avalonia.ViewModels;

/// <summary>
/// ViewModel for receiving data, responsible for display logic and conversion.
/// Implements IDisposable to ensure event subscriptions are cleaned up.
/// </summary>
public class MessageReceiverViewModel : ViewModelBase, IDisposable
{
    private readonly ISerialPortService _serialPortService = null!;
    public MessagingStateViewModel State { get; } = null!;

    private string _receivedDataText = string.Empty;
    public string ReceivedDataText
    {
        get => _receivedDataText;
        private set => this.RaiseAndSetIfChanged(ref _receivedDataText, value);
    }

    // New: auto-scroll toggle exposed on VM so UI can bind and persist behavior
    private bool _autoScrollEnabled = true;
    public bool AutoScrollEnabled
    {
        get => _autoScrollEnabled;
        set => this.RaiseAndSetIfChanged(ref _autoScrollEnabled, value);
    }

    public ReactiveCommand<Unit, Unit> ClearReceivedDataCommand { get; }

    // Track subscription so we can unsubscribe on Dispose
    private NotifyCollectionChangedEventHandler? _sentMessagesHandler;

    public MessageReceiverViewModel()
        : this(SerialPortService.Null, new MessagingStateViewModel())
    {
    }

    public MessageReceiverViewModel(ISerialPortService serialPortService, MessagingStateViewModel state)
    {
        _serialPortService = serialPortService ?? SerialPortService.Null;
        State = state ?? new MessagingStateViewModel();

        // Subscribe to incoming data from the serial service
        _serialPortService.DataReceived += OnDataReceived;

        // Observe sent-message history so receiver can also display previously sent items
        _sentMessagesHandler = new NotifyCollectionChangedEventHandler((s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is string text)
                    {
                        // Append the sent message into ReceivedDataText with a newline
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

        GC.SuppressFinalize(this);
    }
}
