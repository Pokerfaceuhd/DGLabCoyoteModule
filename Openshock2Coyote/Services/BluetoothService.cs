using InTheHand.Bluetooth;

namespace openshock2coyote.Services;

public static class BluetoothService
{
    public static async Task<IReadOnlyCollection<BluetoothDevice>> GetBluetoothDevices(CancellationToken cancellationToken = default)
    {
        return await Bluetooth.ScanForDevicesAsync(cancellationToken:cancellationToken);
    }
}