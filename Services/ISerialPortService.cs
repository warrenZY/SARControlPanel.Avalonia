using SARControlPanel.Avalonia.ViewModels;
using System;
using System.Threading.Tasks;

namespace SARControlPanel.Avalonia.Services;

/// <summary>
/// Defines the contract for serial port communication services.
/// Used to decouple ViewModels from the concrete serial port implementation.
/// </summary>
public interface ISerialPortService
{
    /// <summary>
    /// Event fired when the connection state changes.
    /// </summary>
    event Action<SerialConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// Event fired when data is received from the serial port.
    /// The payload is the raw received byte array.
    /// </summary>
    event Action<byte[]>? DataReceived;

    /// <summary>
    /// Sends data asynchronously through the serial port.
    /// </summary>
    /// <param name="data">The byte array to send.</param>
    /// <returns>The number of bytes sent.</returns>
    Task<int> SendDataAsync(byte[] data);

    /// <summary>
    /// Register a concrete System.IO.Ports.SerialPort instance with the service.
    /// The service will subscribe to DataReceived on the provided port and forward events.
    /// Passing null will unregister the current port.
    /// </summary>
    /// <param name="port">The SerialPort instance to register, or null to unregister.</param>
    void RegisterSerialPort(System.IO.Ports.SerialPort? port);
}
