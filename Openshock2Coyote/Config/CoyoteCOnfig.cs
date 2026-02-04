namespace openshock2coyote.Config;

public sealed class CoyoteConfig
{
    public string CoyoteAddress { get; set; } = String.Empty;
    public bool AutoConnect { get; set; } = false;
    public int DutyCycle { get; set; } = 50;
    public bool Vibrate { get; set; } = true;
    public JsonRange<float> VibrateMultiplierRange { get; set; } = new() { Min = 0.1f, Max = 0.2f};
    public JsonRange<float> ShockMultiplierRange { get; set; } = new() { Min = 0.05f, Max = 1.0f };
    public BFDirectiveConfig BfDirective { get; set; } = new();
}