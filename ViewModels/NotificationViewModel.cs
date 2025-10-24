using SARControlPanel.Avalonia.Services;
using ObservableCollections;

namespace SARControlPanel.Avalonia.ViewModels
{
    public class NotificationViewModel : ViewModelBase
    {
        /// <summary>
        /// Exposes the collection view from the NotificationService for the UI to bind to.
        /// </summary>
        public INotifyCollectionChangedSynchronizedViewList<NotificationMessage> MessagesView => NotificationService.Instance.NotificationMessages;
    }
}