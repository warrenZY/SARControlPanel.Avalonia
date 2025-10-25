using Avalonia.Controls;
using Avalonia.Input;
using SARControlPanel.Avalonia.ViewModels;
using System.Reactive; // for Unit
using System.Reactive.Threading.Tasks; // for ToTask()

namespace SARControlPanel.Avalonia.Controls;

/// <summary>
/// Responsible for the user control that handles serial data transmission.
/// </summary>
public partial class DevicesMessageSenderControl : UserControl
{
    public DevicesMessageSenderControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles the Enter key press event to trigger the SendCommand.
    /// </summary>
    public async void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ensure only Enter key is processed and no modifier keys are active
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MessageSenderViewModel vm)
        {
            try
            {
                // Await the ReactiveCommand execution using ToTask() to avoid relying on Subscribe overloads.
                await vm.SendCommand.Execute(Unit.Default).ToTask();
            }
            catch
            {
                // Swallow or log errors as appropriate. Avoid throwing from UI event handler.
            }

            e.Handled = true; // Prevent the TextBox from adding a newline
        }
    }
}
