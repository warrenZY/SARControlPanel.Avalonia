using ReactiveUI;
using System;

namespace SARControlPanel.Avalonia;

public class ScalingService : ReactiveObject
{
    private static readonly ScalingService _instance = new ScalingService();
    
    public static ScalingService Instance => _instance;

    private double _scaleFactor = 1.0;
    
    public double ScaleFactor
    {
        get => _scaleFactor;
        set
        {
            var newValue = Math.Max(0.5, Math.Min(5.0, value));
            if (_scaleFactor != newValue)
            {
                _scaleFactor = newValue;
                this.RaisePropertyChanged(nameof(ScaleFactor));
                this.RaisePropertyChanged(nameof(ScaledFontSize));
                ScaleChanged?.Invoke(_scaleFactor);
            }
        }
    }

    public double BaseFontSize => 14;
    
    public double ScaledFontSize => BaseFontSize * ScaleFactor;

    public event Action<double>? ScaleChanged;

    private ScalingService() { }
}