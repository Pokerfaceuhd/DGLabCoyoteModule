using openshock2coyote.Config.Triggers;

namespace openshock2coyote.Config;

public class Pawprint
{
    public Boolean Enabled { get; set; } = false;
    public required byte color { get; set; }
    public required BaseTrigger Trigger { get; set; }
}