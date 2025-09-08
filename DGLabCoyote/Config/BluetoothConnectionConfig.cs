namespace DGLabCoyote.Config;

public sealed class BluetoothConnectionConfig
{
    public string CoyoteAddress { get; set; } = String.Empty;
    public Boolean AutoConnect { get; set; } = false;
}