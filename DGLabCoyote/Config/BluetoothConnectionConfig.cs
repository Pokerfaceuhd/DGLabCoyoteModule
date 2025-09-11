namespace DGLabCoyote.Config;

public sealed class BluetoothConnectionConfig
{
    public string CoyoteAddress { get; set; } = String.Empty;
    public byte Frequency { get; set; } = 1;
}