using System.Collections.Concurrent;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using SimpleSharpBLE;

namespace openshock2coyote.Services;

public sealed class BluetoothScanner(ILogger<BluetoothScanner> logger) : IDisposable
{
    private readonly ConcurrentDictionary<string, Peripheral> _devices = new();
    
    private Adapter adapter;

    public IReadOnlyCollection<Peripheral> Devices =>
        _devices.Values.ToArray();

public Task StartAsync()
    {
        if (adapter is not null)
            return Task.CompletedTask;

        var adapters = Adapter.GetAdapters();

        if (adapters.Count == 0)
        {
            logger.LogWarning("No Bluetooth adapters found");
            return Task.CompletedTask;
        }

        // Keep the first adapter alive.
        adapter = adapters[0];

        // Dispose adapters we aren't using.
        for (var i = 1; i < adapters.Count; i++)
            adapters[i].Dispose();

        logger.LogInformation(
            "Using Bluetooth adapter: {Identifier} [{Address}], Powered: {Powered}",
            adapter.Identifier,
            adapter.Address,
            adapter.IsPowered
        );

        adapter.ScanFound += (_, e) =>
        {
            var peripheral = e.Peripheral;

            if (!peripheral.Identifier.StartsWith("47L"))
            {
                peripheral.Dispose();
                return;
            }
            
            logger.LogInformation(
                "Found: {Identifier} [{Address}] {Rssi} dBm",
                peripheral.Identifier,
                peripheral.Address,
                peripheral.Rssi
            );
            
            _devices[peripheral.Address] = peripheral;
        };

        try
        {
            logger.LogInformation("Starting Bluetooth scan");

            adapter.ScanStart();

            logger.LogInformation(
                "Scan active: {Active}",
                adapter.ScanIsActive
            );
        }
        catch
        {
            adapter.Dispose();
            adapter = null;
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (adapter is null)
            return Task.CompletedTask;

        try
        {
            if (adapter.ScanIsActive)
                adapter.ScanStop();
        }
        finally
        {
            adapter.Dispose();
            adapter = null;
        }

        logger.LogInformation("Bluetooth scan stopped");

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAsync();
    }
}