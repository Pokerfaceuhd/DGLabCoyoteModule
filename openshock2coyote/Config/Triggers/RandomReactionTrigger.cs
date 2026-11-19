namespace openshock2coyote.Config.Triggers;

public class RandomReactionTrigger
{
    public const byte TriggerMode = 0x03;

    public byte TriggerEventId { get; set; } = 1;
    public JsonRange<ushort> RandomGreenLightDelaySeconds { get; set; } = new() { Min = 10, Max = 30 };
    public ushort ReactionTimeSeconds { get; set; } = 2;
    public byte ParameterIncreaseOnActivation { get; set; } = 0;
    public byte ParameterIncreaseRateWhileActive { get; set; } = 0;
    public byte ParameterDecreaseOnDeactivation { get; set; } = 0;
    public byte ContinuousParameterDecreaseRateAfterDeactivation { get; set; } = 0;
    public List<Guid> Actions { get; set; } = [];
}