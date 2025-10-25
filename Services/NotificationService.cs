using Avalonia.Threading;
using ObservableCollections; // This is the Cysharp library
using System;

namespace SARControlPanel.Avalonia.Services;

public enum NotificationLevel
{
    Info,
    Warning,
    Error
}

public class NotificationMessage
{
    public DateTime Timestamp { get; } = DateTime.Now;
    public NotificationLevel Level { get; }
    public string Message { get; }

    public NotificationMessage(string message, NotificationLevel level)
    {
        Message = message;
        Level = level;
    }
}

public sealed class NotificationService
{
    private static readonly NotificationService _instance = new();
    public static NotificationService Instance => _instance;

    private const int MaxMessages = 100;

    private readonly ObservableQueue<NotificationMessage> _messageQueue = new();

    public INotifyCollectionChangedSynchronizedViewList<NotificationMessage> NotificationMessages { get; }

    // Constructor to initialize the view
    private NotificationService()
    {
        // This creates the bindable view from your backing queue
        NotificationMessages = _messageQueue.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    }

    public void AddMessage(string message, NotificationLevel level)
    {
        var notification = new NotificationMessage(message, level);
        Dispatcher.UIThread.Post(() =>
        {
            _messageQueue.Enqueue(notification);
            while (_messageQueue.Count > MaxMessages)
            {
                _messageQueue.Dequeue();
            }
        });
    }
}