using Avalonia.Data;
using Avalonia.Data.Converters;
using SARControlPanel.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SARControlPanel.Avalonia.Converters;

public class ConnectionStateToErrorVisibilityConverter : IValueConverter
{
    public static readonly ConnectionStateToErrorVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SerialConnectionState state)
        {
            return state == SerialConnectionState.Error;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return new BindingNotification(new NotSupportedException(), BindingErrorType.Error);
    }
}

