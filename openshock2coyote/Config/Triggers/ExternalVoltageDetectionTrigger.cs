using System.Text.Json.Serialization;

namespace openshock2coyote.Config.Triggers;

public class ExternalVoltageDetectionTrigger : BaseTrigger
{
    public override byte TriggerMode => 0x0F;

    public required byte TriggerEventId { get; set; }
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
}

public enum InternalPullUp
{
    HighLevel = 0,
    HighImpedance = 1
}