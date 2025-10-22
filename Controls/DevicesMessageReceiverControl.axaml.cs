using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using SARControlPanel.Avalonia.ViewModels;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace SARControlPanel.Avalonia.Controls
{
    /// <summary>
    /// Responsible for the user control that handles serial data reception.
    /// </summary>
    public partial class DevicesMessageReceiverControl : UserControl
    {
        private readonly CompositeDisposable _disposables = new();
        private TextBox? _receivedTextBox;

        public DevicesMessageReceiverControl()
        {
            InitializeComponent();
            _receivedTextBox = this.FindControl<TextBox>("ReceivedTextBox");

            // react to DataContext changes to (re)attach subscriptions
            this.DataContextChanged += (_, _) => AttachSubscriptions();
            // initial attach if DataContext already set (design-time)
            AttachSubscriptions();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void AttachSubscriptions()
        {
            _disposables.Clear();

            if (DataContext is MessageReceiverViewModel vm)
            {
                // Subscribe to ReceivedDataText changes and auto-scroll when enabled.
                var sub = vm.WhenAnyValue(x => x.ReceivedDataText)
                            .ObserveOn(RxApp.MainThreadScheduler)
                            .Subscribe(_ =>
                            {
                                if (vm.AutoScrollEnabled && _receivedTextBox is not null)
                                {
                                    // set caret to end to cause the TextBox to scroll to the end
                                    var len = _receivedTextBox.Text?.Length ?? 0;
                                    _receivedTextBox.CaretIndex = len;
                                }
                            });

                _disposables.Add(sub);

                // Also watch AutoScrollEnabled change to allow immediate scrolling if turned on
                var autoSub = vm.WhenAnyValue(x => x.AutoScrollEnabled)
                                .ObserveOn(RxApp.MainThreadScheduler)
                                .Subscribe(enabled =>
                                {
                                    if (enabled && _receivedTextBox is not null)
                                    {
                                        var len = _receivedTextBox.Text?.Length ?? 0;
                                        _receivedTextBox.CaretIndex = len;
                                    }
                                });

                _disposables.Add(autoSub);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _disposables.Dispose();
        }
    }
}