using OpenShock.Desktop.ModuleBase.Models;
using OpenShock.Serialization.Types;

namespace openshock2coyote.Config.Actions;

public class OpenshockAction : BaseAction
{
    public ControlType Type { get; set; } = ControlType.Shock;
    public List<Guid> Shockers { get; set; } = [];
    public JsonRange<byte> IntensityRange { get; set; } = new () { Min = 0, Max = 100 };
    public byte DefaultIntensity { get; set; } = 20;
}
