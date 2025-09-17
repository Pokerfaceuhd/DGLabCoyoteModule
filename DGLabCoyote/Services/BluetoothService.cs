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
    
    public void Dispose()
    {
        
    }
}