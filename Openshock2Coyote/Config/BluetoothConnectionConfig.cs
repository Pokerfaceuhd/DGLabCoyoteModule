namespace openshock2coyote.Config;

public sealed class BluetoothConnectionConfig
{
    public string CoyoteAddress { get; set; } = String.Empty;
    public int FrequencyMs { get; set; } = 1;
    public bool AutoConnect { get; set; } = true;
}