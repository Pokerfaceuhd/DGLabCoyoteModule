namespace DGLabCoyote.Config;

public sealed class HubConfig
{
    public Guid? Hub { get; set; } = null;
    public ushort ChannelAId = 11111;
    public ushort ChannelBId = 22222;
}