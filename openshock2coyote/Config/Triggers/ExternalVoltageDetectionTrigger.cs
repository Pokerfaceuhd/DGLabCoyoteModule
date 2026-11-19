using System.Text.Json.Serialization;

namespace openshock2coyote.Config.Triggers;

public class ExternalVoltageDetectionTrigger
{
    public const byte TriggerMode = 0x0F;

    public byte TriggerEventId { get; set; } = 1;
    public InternalPullUp InternalPullUp { get; set; } = InternalPullUp.HighLevel;
    
    [JsonIgnore]
    public bool IsHighImpedance
    {
        get => InternalPullUp == InternalPullUp.HighImpedance;
        set => InternalPullUp = value
            ? InternalPullUp.HighImpedance
            : InternalPullUp.HighLevel;
    }
    
    public JsonRange<byte> TargetVoltageRange { get; set; } = new() { Min = 100, Max = 255 };
    public byte ParameterMappingRange { get; set; } = 0;
    public List<Guid> Actions { get; set; } = [];
}

public enum InternalPullUp
{
    HighLevel = 0,
    HighImpedance = 1
}