namespace openshock2coyote.Config;

public sealed class CoyoteConfig
{
    public string CoyoteAddress { get; set; } = String.Empty;
    public int DutyCycle { get; set; } = 50;
    public bool Vibrate { get; set; } = true;
    public float VibrateMultiplier { get; set; } = 0.1f;
    public float ShockMultiplier { get; set; } = 1.0f;
}