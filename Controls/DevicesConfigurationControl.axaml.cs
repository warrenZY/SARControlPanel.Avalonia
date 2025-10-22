using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SARControlPanel.Avalonia.Controls;

public partial class DevicesConfigurationControl : UserControl
{
    public DevicesConfigurationControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}