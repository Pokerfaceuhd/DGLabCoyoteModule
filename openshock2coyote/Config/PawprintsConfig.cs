using openshock2coyote.Config.Actions;

namespace openshock2coyote.Config;

public class PawprintsConfig
{
    public bool AutoConnect { get; set; } = true;
    public Pawprint[] Pawprints { get; set; } =
    [
        new() { Color = 1 }, 
        new() { Color = 2 }, 
        new() { Color = 3 }, 
        new() { Color = 4 }, 
        new() { Color = 5 },
        new() { Color = 6 }
    ];
    public List<BaseAction> Actions { get; set; } = [];
}