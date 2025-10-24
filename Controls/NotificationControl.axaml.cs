using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SARControlPanel.Avalonia.Controls
{
    public partial class NotificationControl : UserControl
    {
        public NotificationControl()
        {
            InitializeComponent();
        }

        // Corrected: Explicitly define InitializeComponent to load the XAML.
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}