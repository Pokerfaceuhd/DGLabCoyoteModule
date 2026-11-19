using openshock2coyote.Config.Triggers;

namespace openshock2coyote.Config;

public class Pawprint
{
    public bool Enabled { get; set; }
    public required byte Color { get; set; }
    public byte TriggerMode { get; set; } = 0x00;
    public ExternalVoltageDetectionTrigger ExternalVoltageTrigger { get; set; } = new();
    public PressReleaseTriggerAndAccelerationAngleTrigger PressReleaseTriggerAndAccelerationAngleTrigger { get; set; } = new();
    public ProbabilityTrigger ProbabilityTrigger { get; set; } = new();
    public RandomReactionTrigger RandomReactionTrigger { get; set; } = new();
}