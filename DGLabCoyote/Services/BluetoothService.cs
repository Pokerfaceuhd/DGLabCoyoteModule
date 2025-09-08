using InTheHand.Bluetooth;

namespace DGLabCoyote.Services;

public sealed class BluetoothService : IDisposable
{

    public BluetoothService()
    {

    }

    public async Task<IReadOnlyCollection<BluetoothDevice>> GetBluetoothDevices()
    {
        return await Bluetooth.ScanForDevicesAsync();
    }

    public void Dispose()
    {
        
    }
}