using SARControlPanel.Avalonia.Services;

namespace SARControlPanel.Avalonia.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        // Devices configuration VM already exposed for the left pane
        public DevicesConfigurationViewModel DevicesConfigurationViewModel { get; } = new DevicesConfigurationViewModel();

        // Shared messaging state used by both sender and receiver controls
        public MessagingStateViewModel MessagingState { get; }

        // Sender / Receiver ViewModels exposed so XAML can bind their DataContexts
        public MessageSenderViewModel MessageSenderViewModel { get; }
        public MessageReceiverViewModel MessageReceiverViewModel { get; }

        public NotificationViewModel NotificationViewModel { get; } = new NotificationViewModel();

        public MainWindowViewModel()
        {
            // Create a single shared state instance so HEX mode and counters are synchronized
            MessagingState = new MessagingStateViewModel();

            // Use the runtime serial service singleton so both VMs observe the same ISerialPortService.
            // Use SerialPortService.Null for design-time inside those VMs if needed; here we pass the runtime instance.
            var serialService = SerialPortService.Instance;

            MessageSenderViewModel = new MessageSenderViewModel(serialService, MessagingState);
            MessageReceiverViewModel = new MessageReceiverViewModel(serialService, MessagingState);
        }
    }
}
