using System.Text.Json.Serialization;

namespace openshock2coyote.Config.Triggers;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(NothingMode), "nothing")]
[JsonDerivedType(typeof(RandomReactionTrigger), "random")]
[JsonDerivedType(typeof(ProbabilityTrigger), "probability")]
[JsonDerivedType(typeof(PressReleaseTriggerAndAccelerationAngleTrigger), "pressReleaseAcceleration")]
[JsonDerivedType(typeof(ExternalVoltageDetectionTrigger), "externalVoltage")]
[JsonDerivedType(typeof(RawMode), "raw")]
public abstract class BaseTrigger
{
    public abstract byte TriggerMode { get; }
    public List<Guid> Actions { get; set; } = [];
}