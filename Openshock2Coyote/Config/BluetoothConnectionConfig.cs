namespace openshock2coyote.Config;

public sealed class BluetoothConnectionConfig
{
    public string CoyoteAddress { get; set; } = String.Empty;
    public int FrequencyMs { get; set; } = 50;
}