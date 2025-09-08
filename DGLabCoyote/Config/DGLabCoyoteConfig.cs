namespace DGLabCoyote.Config;

public sealed class DgLabCoyoteConfig
{
    public HubConfig Hub { get; set; } = new();
    
    public BluetoothConnectionConfig BluetoothConnection { get; set; } = new();
}