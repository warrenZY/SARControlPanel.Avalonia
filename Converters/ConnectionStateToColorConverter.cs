using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SARControlPanel.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SARControlPanel.Avalonia.Converters;

public class ConnectionStateToColorConverter : IValueConverter
{
    public static readonly ConnectionStateToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SerialConnectionState state)
        {
            return state switch
            {
                SerialConnectionState.Connected => Brushes.Green,
                SerialConnectionState.Connecting => Brushes.Yellow,
                SerialConnectionState.Disconnected => Brushes.Gray,
                SerialConnectionState.Error => Brushes.Red,
                _ => Brushes.Gray,
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(new NotSupportedException(), BindingErrorType.Error);
    }
}