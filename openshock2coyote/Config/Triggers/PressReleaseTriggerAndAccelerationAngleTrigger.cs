using System.Text.Json.Serialization;

namespace openshock2coyote.Config.Triggers;

public class PressReleaseTriggerAndAccelerationAngleTrigger
{
    public const byte TriggerMode = 0x05;

    [JsonIgnore]
    public byte PressReleaseTriggerEventId = 1;
    public PressReleaseTriggerMode PressReleaseMode { get; set; } = PressReleaseTriggerMode.Press;
    
    [JsonIgnore]
    public bool IsPressMode
    {
        get => PressReleaseMode == PressReleaseTriggerMode.Press;
        set => PressReleaseMode = value
            ? PressReleaseTriggerMode.Press
            : PressReleaseTriggerMode.Release;
    }
    
    public byte ActivationDebounce { get; set; }
    public byte DeactivationDebounce { get; set; }
    public byte PressReleaseParameterIncreaseRateWhileActive { get; set; } 
    public byte PressReleaseParameterDecreaseOnDeactivation { get; set; } = byte.MaxValue;
    public byte PressReleaseParameterDecreaseRateAfterDeactivation { get; set; }
    public byte PressReleaseParameterIncreaseOnActivation { get; set; }
    public List<Guid> ActionsOnPressRelease { get; set; } = [];
    
    public AccelerationAngleSubmode AccelerationOrAngleSubmode { get; set; } = AccelerationAngleSubmode.Disabled;
    
    [JsonIgnore]
    public byte AccelerationAngleTriggerEventId = 2;
    public AccelerationDetectionMode AccelerationDetectionMode { get; set; } = AccelerationDetectionMode.Above;
    
    [JsonIgnore]
    public bool IsAboveAccelerationMode
    {
        get => AccelerationDetectionMode == AccelerationDetectionMode.Above;
        set => AccelerationDetectionMode = value
            ? AccelerationDetectionMode.Above
            : AccelerationDetectionMode.Below;
    }
    
    public ushort AccelerationThreshold { get; set; } = 10;
    public byte AccelerationAngleParameterMappingRange { get; set; }
    public JsonRange<sbyte> RangeX {get; set;} = new() { Min = -128, Max = 127 };
    public JsonRange<sbyte> RangeY {get; set;} = new() { Min = -128, Max = 127 };
    public JsonRange<sbyte> RangeZ {get; set;} = new() { Min = -128, Max = 127 };
    public List<Guid> ActionsOnAngleAcceleration { get; set; } = [];
}

public enum AccelerationAngleSubmode
{
    Acceleration = 0,
    Angle = 1,
    Disabled = 2
}

public enum PressReleaseTriggerMode
{
    Press = 0,
    Release = 1
}

public enum AccelerationDetectionMode
{
    Below = 0,
    Above = 1
}