using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SARControlPanel.Avalonia.Services;
using System;
using System.Linq;
using System.Reactive.Linq;

namespace SARControlPanel.Avalonia.Controls;

public partial class NotificationControl : UserControl
{
    private IDisposable? _collectionSubscription;
    private Window? _parentWindow;

    public NotificationControl()
    {
        InitializeComponent();

        // Subscribe to parent window when attached to visual tree
        this.AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Get parent window and subscribe to its pointer events
        _parentWindow = this.FindAncestorOfType<Window>();
        if (_parentWindow != null)
        {
            _parentWindow.PointerReleased += OnGlobalPointerReleased;
        }
    }

    private void OnGlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Check if click is outside this control
        var sourceVisual = e.Source as Visual;
        if (sourceVisual != null && !this.IsVisualAncestorOf(sourceVisual))
        {
            NotificationListBox.SelectedItem = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _collectionSubscription?.Dispose();

        if (DataContext is ViewModels.NotificationViewModel viewModel)
        {
            _collectionSubscription = Observable
                .FromEventPattern<System.Collections.Specialized.NotifyCollectionChangedEventHandler, System.Collections.Specialized.NotifyCollectionChangedEventArgs>(
                    h => viewModel.MessagesView.CollectionChanged += h,
                    h => viewModel.MessagesView.CollectionChanged -= h)
                .Where(_ => viewModel.AutoScrollEnabled)
                .Subscribe(_ => ScrollToBottom());
        }
    }

    private void ScrollToBottom()
    {
        if (NotificationListBox?.Items?.Count > 0)
        {
            NotificationListBox.GetVisualDescendants()
                              .OfType<ScrollViewer>()
                              .FirstOrDefault()?
                              .ScrollToEnd();
        }
    }


    private async void OnCopyMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem &&
            menuItem.DataContext is NotificationMessage selectedNotification)
        {
            try
            {
                var textToCopy = $"[{selectedNotification.Timestamp:HH:mm:ss}] {selectedNotification.Level}: {selectedNotification.Message}";
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(textToCopy);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _collectionSubscription?.Dispose();

        this.AttachedToVisualTree -= OnAttachedToVisualTree;

        if (_parentWindow != null)
        {
            _parentWindow.PointerReleased -= OnGlobalPointerReleased;
        }

        base.OnDetachedFromVisualTree(e);
    }
}