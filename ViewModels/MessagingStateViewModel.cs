using ReactiveUI;
using System.Reactive;
using System.Collections.ObjectModel;

namespace SARControlPanel.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel to hold and manage state shared between the sender and receiver,
    /// primarily the HEX mode status, byte counters and sent-message history.
    /// </summary>
    public class MessagingStateViewModel : ViewModelBase
    {
        // Shared State Properties
        private bool _isHexMode = true;
        /// <summary>
        /// Gets or sets a value indicating whether data should be interpreted/sent as HEX bytes.
        /// </summary>
        public bool IsHexMode
        {
            get => _isHexMode;
            set => this.RaiseAndSetIfChanged(ref _isHexMode, value);
        }

        private long _bytesSentCount;
        /// <summary>
        /// Gets or sets the total number of bytes successfully sent.
        /// </summary>
        public long BytesSentCount
        {
            get => _bytesSentCount;
            set => this.RaiseAndSetIfChanged(ref _bytesSentCount, value);
        }

        private long _bytesReceivedCount;
        /// <summary>
        /// Gets or sets the total number of bytes received.
        /// </summary>
        public long BytesReceivedCount
        {
            get => _bytesReceivedCount;
            set => this.RaiseAndSetIfChanged(ref _bytesReceivedCount, value);
        }

        /// <summary>
        /// A small collection storing textual representations of sent messages.
        /// The receiver VM can observe this collection to display previously sent items.
        /// </summary>
        public ObservableCollection<string> SentMessages { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Command to reset the sent and received byte counters.
        /// </summary>
        public ReactiveCommand<Unit, Unit> ResetCountsCommand { get; }

        public MessagingStateViewModel()
        {
            ResetCountsCommand = ReactiveCommand.Create(() =>
            {
                BytesSentCount = 0;
                BytesReceivedCount = 0;
                SentMessages.Clear();
            });
        }
    }
}
