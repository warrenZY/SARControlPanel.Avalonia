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

/// <summary>
/// Represents a notification message with timestamp, level, and content.
/// </summary>
public class NotificationMessage
{
    public DateTime Timestamp { get; } = DateTime.Now;
    public NotificationLevel Level { get; }
    public string Message { get; }

    /// <summary>
    /// Initializes a new notification message.
    /// </summary>
    /// <param name="message">The message text content.</param>
    /// <param name="level">The notification severity level.</param>
    public NotificationMessage(string message, NotificationLevel level)
    {
        Message = message;
        Level = level;
    }
}

/// <summary>
/// Singleton service for managing application-wide notifications.
/// Maintains a queue of notifications and enforces a maximum message limit.
/// </summary>
public sealed class NotificationService
{
    private static readonly NotificationService _instance = new();
    
    public static NotificationService Instance => _instance;

    private const int MaxMessages = 100;

    private readonly ObservableQueue<NotificationMessage> _messageQueue = new();

    public INotifyCollectionChangedSynchronizedViewList<NotificationMessage> NotificationMessages { get; }

    private NotificationService()
    {
        NotificationMessages = _messageQueue.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    }

    /// <summary>
    /// Adds a new notification message to the queue and removes old messages if the limit is exceeded.
    /// Posts the operation to the UI thread to ensure thread safety.
    /// </summary>
    /// <param name="message">The message text content.</param>
    /// <param name="level">The notification severity level.</param>
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