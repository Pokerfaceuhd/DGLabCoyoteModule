namespace openshock2coyote.Config.Triggers;

public class ProbabilityTrigger
{
    public const byte TriggerMode = 0x04;

    public byte[] TriggerIds { get; set; } = [1,2,3,4,5,6];
    public byte[] TriggerProbabilities { get; set; } = [33,0,0,0,0,0];
    public List<Guid>[] ProbabilityActions { get; set; } = [new(),new(),new(),new(),new(),new()];
    public ushort CooldownSeconds { get; set; } = 0;
}