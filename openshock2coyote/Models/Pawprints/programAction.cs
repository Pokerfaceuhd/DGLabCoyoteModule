using openshock2coyote.Config.Actions;

namespace openshock2coyote.Models.Pawprints;

public class ProgramAction(BaseAction action)
{
    public bool Active = false;
    public BaseAction Action = action;
}