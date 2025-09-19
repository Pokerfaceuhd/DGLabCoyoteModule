namespace openshock2coyote.Config;

public sealed class HubConfig
{
    public Guid Hub { get; set; } = Guid.Empty;
    public ushort ChannelAId = 11111;
    public ushort ChannelBId = 22222;
}