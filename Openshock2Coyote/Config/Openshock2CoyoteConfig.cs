namespace openshock2coyote.Config;

public sealed class Openshock2CoyoteConfig
{
    public HubConfig Hub { get; set; } = new();
    
    public BluetoothConnectionConfig BluetoothConnection { get; set; } = new();
}