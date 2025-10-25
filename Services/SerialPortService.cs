using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace SARControlPanel.Avalonia.Services;

/// <summary>
/// A lightweight singleton serial port service that forwards incoming data
/// and exposes a send API. Designed to be registered with a SerialPort
/// instance created/managed elsewhere (for example, DevicesConfigurationViewModel).
/// </summary>
public sealed class SerialPortService : ISerialPortService
{
    private static readonly Lazy<SerialPortService> _lazy = new(() => new SerialPortService());
    public static SerialPortService Instance => _lazy.Value;

    // A trivial "null" implementation useful for design-time and tests
    public static ISerialPortService Null => new NullSerialPortService();

    private SerialPort? _serialPort;

    public event Action<byte[]>? DataReceived;
    public event Action<ViewModels.SerialConnectionState>? ConnectionStateChanged;

    private SerialPortService() { }

    public void RegisterSerialPort(SerialPort? port)
    {
        if (_serialPort != null)
        {
            try
            {
                _serialPort.DataReceived -= OnSerialDataReceived;
            }
            catch
            {
                // ignore
            }
        }

        _serialPort = port;

        if (_serialPort != null)
        {
            _serialPort.DataReceived += OnSerialDataReceived;
            ConnectionStateChanged?.Invoke(ViewModels.SerialConnectionState.Connected);
        }
        else
        {
            ConnectionStateChanged?.Invoke(ViewModels.SerialConnectionState.Disconnected);
        }
    }

    private void OnSerialDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var sp = sender as SerialPort ?? _serialPort;
            if (sp == null) return;

            int bytesToRead = sp.BytesToRead;
            if (bytesToRead <= 0) return;

            byte[] buffer = new byte[bytesToRead];
            int read = sp.Read(buffer, 0, bytesToRead);

            if (read > 0)
            {
                if (read != buffer.Length)
                {
                    var resized = new byte[read];
                    Array.Copy(buffer, resized, read);
                    DataReceived?.Invoke(resized);
                }
                else
                {
                    DataReceived?.Invoke(buffer);
                }
            }
        }
        catch
        {
            // swallow exceptions from event thread to avoid crashing the runtime
        }
    }

    public async Task<int> SendDataAsync(byte[] data)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        try
        {
            await Task.Run(() =>
            {
                _serialPort.Write(data, 0, data.Length);
            }).ConfigureAwait(false);

            return data.Length;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to send data.", ex);
        }
    }

    // Very small null implementation for design-time safety
    private class NullSerialPortService : ISerialPortService
    {
        // Use explicit add/remove accessors to avoid CS0067 (event declared but never used)
        private Action<byte[]>? _dataReceived;
        public event Action<byte[]>? DataReceived
        {
            add { _dataReceived += value; }
            remove { _dataReceived -= value; }
        }

        private Action<ViewModels.SerialConnectionState>? _connectionStateChanged;
        public event Action<ViewModels.SerialConnectionState>? ConnectionStateChanged
        {
            add { _connectionStateChanged += value; }
            remove { _connectionStateChanged -= value; }
        }

        public Task<int> SendDataAsync(byte[] data) => Task.FromResult(data?.Length ?? 0);
        public void RegisterSerialPort(SerialPort? port) { /* no-op */ }
    }
}