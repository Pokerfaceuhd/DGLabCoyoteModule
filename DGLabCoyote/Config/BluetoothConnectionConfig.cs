namespace DGLabCoyote.Config;

public sealed class BluetoothConnectionConfig
{
    public string CoyoteAddress { get; set; } = String.Empty;
    public int FrequencyMs { get; set; } = 1;
}