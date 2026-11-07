using System.Text.Json.Serialization;
using openshock2coyote.Config.Actions;

namespace openshock2coyote.Config.Triggers;

public class PressReleaseTriggerAndAccelerationAngleTrigger : BaseTrigger
{
    public override byte TriggerMode => 0x05;

    public required byte PressReleaseTriggerEventId { get; set; } = 1;
    public PressReleaseTriggerMode PressReleaseMode { get; set; } = PressReleaseTriggerMode.Press;
    
    [JsonIgnore]
    public bool IsPressMode
    {
        get => PressReleaseMode == PressReleaseTriggerMode.Press;
        set => PressReleaseMode = value
            ? PressReleaseTriggerMode.Press
            : PressReleaseTriggerMode.Release;
    }
    
    public byte ActivationDebounce { get; set; } = 0;
    public byte DeactivationDebounce { get; set; } = 0;
    public byte PressReleaseParameterIncreaseRateWhileActive { get; set; } = 0;
    public byte PressReleaseParameterDecreaseOnDeactivation { get; set; } = 0;
    public byte PressReleaseParameterDecreaseRateAfterDeactivation { get; set; } = 0;
    public byte PressReleaseParameterIncreaseOnActivation { get; set; } = 0;
    public List<Guid> ActionsOnPressRelease { get; set; } = [];
    
    public AccelerationAngleSubmode AccelerationOrAngleSubmode { get; set; } = AccelerationAngleSubmode.Angle;
    
    [JsonIgnore]
    public bool IsAngleSubmode
    {
        get => AccelerationOrAngleSubmode == AccelerationAngleSubmode.Angle;
        set => AccelerationOrAngleSubmode = value
            ? AccelerationAngleSubmode.Angle
            : AccelerationAngleSubmode.Acceleration;
    }

    public required byte AccelerationAngleTriggerEventId { get; set; } = 2;
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
    public byte AccelerationAngleParameterMappingRange { get; set; } = 0;
    public JsonRange<sbyte> RangeX {get; set;} = new() { Min = -128, Max = 127 };
    public JsonRange<sbyte> RangeY {get; set;} = new() { Min = -128, Max = 127 };
    public JsonRange<sbyte> RangeZ {get; set;} = new() { Min = -128, Max = 127 };
    public List<Guid> ActionsOnAngleAcceleration { get; set; } = [];
}

public enum AccelerationAngleSubmode
{
    Acceleration = 0,
    Angle = 1
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