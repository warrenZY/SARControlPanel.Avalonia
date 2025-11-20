using SARControlPanel.Avalonia.Services;
using ObservableCollections;
using ReactiveUI;
using Avalonia.Controls;

namespace SARControlPanel.Avalonia.ViewModels;

public class NotificationViewModel : ViewModelBase
{
    public INotifyCollectionChangedSynchronizedViewList<NotificationMessage> MessagesView => NotificationService.Instance.NotificationMessages;

    private bool _autoScrollEnabled = true;
    
    public bool AutoScrollEnabled
    {
        get => _autoScrollEnabled;
        set => this.RaiseAndSetIfChanged(ref _autoScrollEnabled, value);
    }

    public NotificationViewModel()
    {
        if (Design.IsDesignMode)
        {
            NotificationService.Instance.AddMessage("Design-time Info Message", NotificationLevel.Info);
            NotificationService.Instance.AddMessage("Design-time Warning Message", NotificationLevel.Warning);
            NotificationService.Instance.AddMessage("Design-time Error Message", NotificationLevel.Error);
        }
    }
}