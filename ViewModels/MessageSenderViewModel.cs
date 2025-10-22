using ReactiveUI;
using SARControlPanel.Avalonia.Services;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace SARControlPanel.Avalonia.ViewModels
{
    public class MessageSenderViewModel : ViewModelBase
    {
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

        // Design-time friendly ctor
        public MessageSenderViewModel()
            : this(SerialPortService.Null, new MessagingStateViewModel())
        {
        }

        public MessageSenderViewModel(ISerialPortService serialService, MessagingStateViewModel state)
        {
            _serialService = serialService ?? SerialPortService.Null;
            State = state ?? new MessagingStateViewModel();

            var canSend = this.WhenAnyValue(vm => vm.DataToSend)
                              .Select(text => !string.IsNullOrWhiteSpace(text));

            SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);

            this.WhenAnyValue(vm => vm.DataToSend)
                .Subscribe(_ =>
                {
                    SendErrorMessage = null;
                    HasSendError = false;
                });
        }

        private async Task SendAsync()
        {
            if (string.IsNullOrWhiteSpace(DataToSend))
                return;

            try
            {
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

                // Update UI state on UI thread
                Dispatcher.UIThread.Post(() =>
                {
                    State.BytesSentCount += sent;
                    SendErrorMessage = null;
                    HasSendError = false;

                    // Record sent message in shared history (formatted according to current mode)
                    string formatted;
                    if (State.IsHexMode)
                        formatted = string.Concat(payload.Select(b => b.ToString("X2") + " "));
                    else
                        formatted = Encoding.ASCII.GetString(payload);

                    // Prefix to make clear this is TX
                    State.SentMessages.Add($"TX: {formatted.TrimEnd()}");
                });
            }
            catch (Exception ex)
            {
                UpdateSendError($"Send failed: {ex.Message}");
            }
        }

        private void UpdateSendError(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SendErrorMessage = message;
                HasSendError = true;
            });
        }

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
    }
}