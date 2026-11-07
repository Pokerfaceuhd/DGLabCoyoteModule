using System.Collections.Concurrent;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Shiny;
using Shiny.BluetoothLE;

namespace openshock2coyote.Services;

public sealed class BluetoothScanner(IBleManager bleManager,
    ILogger<BluetoothScanner> logger) : IDisposable
{
    private readonly ConcurrentDictionary<string, IPeripheral> _devices = new();

    private IDisposable? _scanSubscription;

    public IReadOnlyCollection<IPeripheral> Devices =>
        _devices.Values.ToArray();

    public async Task StartAsync()
    {
        if (_scanSubscription != null)
            return;

        var access = await bleManager.RequestAccessAsync();

        if (access != AccessState.Available)
            throw new InvalidOperationException(
                $"Bluetooth is unavailable: {access}");

        _scanSubscription = bleManager.Scan().Subscribe(
            result =>
            {
                var peripheral = result.Peripheral;

                if (peripheral.Name == null || !peripheral.Name.StartsWith("47L"))
                {
                    return;
                }
                
                logger.LogInformation("found {DeviceName}" , peripheral.Name);
                
                _devices[peripheral.Uuid] = peripheral;
            },
            ex => Console.Error.WriteLine(
                $"Pawprint scanning failed: {ex}"));
    }

    public void RemoveDevice(string uuid)
    {
        _devices.Remove(uuid, out _);
    }
    
    public void Stop()
    {
        _scanSubscription?.Dispose();
        _scanSubscription = null;
    }

    public void Dispose()
    {
        Stop();
    }
}