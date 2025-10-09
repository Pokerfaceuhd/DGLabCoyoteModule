namespace openshock2coyote.Config;

public sealed class Openshock2CoyoteConfig
{
    public HubConfig Hub { get; set; } = new();
    
    public CoyoteConfig CoyoteConfig { get; set; } = new();
}