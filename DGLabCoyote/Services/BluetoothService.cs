using InTheHand.Bluetooth;

namespace DGLabCoyote.Services;

public sealed class BluetoothService : IDisposable
{

    public BluetoothService()
    {

    }

    public static async Task<IReadOnlyCollection<BluetoothDevice>> GetBluetoothDevices(CancellationToken cancellationToken)
    {
        return await Bluetooth.ScanForDevicesAsync(cancellationToken:cancellationToken);
    }
    
    public static async Task<bool> CheckDeviceAvailable(String id)
    {
        return await BluetoothDevice.FromIdAsync(id) != null;
    }
    
    public void Dispose()
    {
        
    }
}