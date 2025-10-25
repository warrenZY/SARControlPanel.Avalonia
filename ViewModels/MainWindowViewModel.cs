using SARControlPanel.Avalonia.Services;
using ReactiveUI;
using System.Reactive;

namespace SARControlPanel.Avalonia.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        // Devices configuration VM exposed
        public DevicesConfigurationViewModel DevicesConfigurationViewModel { get; } = new DevicesConfigurationViewModel();
        // Shared messaging state used by both sender and receiver controls
        public MessagingStateViewModel MessagingState { get; }

        // Sender / Receiver ViewModels exposed so XAML can bind their DataContexts
        public MessageSenderViewModel MessageSenderViewModel { get; }
        public MessageReceiverViewModel MessageReceiverViewModel { get; }
        public NotificationViewModel NotificationViewModel { get; } = new NotificationViewModel();

        // Scaling service singleton for UI scaling
        public ScalingService ScalingService => ScalingService.Instance;
        public ReactiveCommand<Unit, Unit> ResetScaleCommand { get; }

        public MainWindowViewModel()
        {
            MessagingState = new MessagingStateViewModel();
            var serialService = SerialPortService.Instance;
            MessageSenderViewModel = new MessageSenderViewModel(serialService, MessagingState);
            MessageReceiverViewModel = new MessageReceiverViewModel(serialService, MessagingState);

            ResetScaleCommand = ReactiveCommand.Create(() =>
            {
                ScalingService.Instance.ScaleFactor = 1.0;
            });
        }
    }
}