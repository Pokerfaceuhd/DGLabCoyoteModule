using openshock2coyote.Config.Actions;
using openshock2coyote.Config.Triggers;

namespace openshock2coyote.Config;

public class PawprintsConfig
{
    public bool AutoConnect { get; set; } = false;
    public Pawprint[] Pawprints { get; set; } =
    [
        new() { color = 1, Trigger = new NothingMode() }, 
        new() { color = 2, Trigger = new NothingMode() }, 
        new() { color = 3, Trigger = new NothingMode() }, 
        new() { color = 4, Trigger = new NothingMode() }, 
        new() { color = 5, Trigger = new NothingMode() },
        new() { color = 6, Trigger = new NothingMode() }
    ];
    public List<BaseAction> Actions { get; set; } = [];
}