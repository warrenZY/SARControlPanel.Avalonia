using SARControlPanel.Avalonia.Services;
using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Linq;
using NLog;

namespace SARControlPanel.Avalonia.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    public DevicesConfigurationViewModel DevicesConfigurationViewModel { get; } = new DevicesConfigurationViewModel();
    public MessagingStateViewModel MessagingState { get; }
    public MessageSenderViewModel MessageSenderViewModel { get; }
    public MessageReceiverViewModel MessageReceiverViewModel { get; }
    public NotificationViewModel NotificationViewModel { get; } = new NotificationViewModel();

    public ScalingService ScalingService => ScalingService.Instance;
    
    public ReactiveCommand<Unit, Unit> ResetScaleCommand { get; }

    private IDisposable? _commandExceptionSubscription;

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

        SubscribeToCommandExceptions();
    }

    private void SubscribeToCommandExceptions()
    {
        try
        {
            _commandExceptionSubscription = ResetScaleCommand.ThrownExceptions.Subscribe(ex =>
            {
                _logger.Error(ex, "ResetScaleCommand error");
                try
                {
                    NotificationService.Instance.AddMessage($"Scale reset error: {ex.Message}", NotificationLevel.Error);
                }
                catch (Exception inner)
                {
                    _logger.Warn(inner, "Failed while handling ResetScaleCommand exception.");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to subscribe to command exceptions.");
        }
    }

    public void Dispose()
    {
        _commandExceptionSubscription?.Dispose();
        DevicesConfigurationViewModel?.Dispose();
        MessageSenderViewModel?.Dispose();
        MessageReceiverViewModel?.Dispose();
        GC.SuppressFinalize(this);
    }
}