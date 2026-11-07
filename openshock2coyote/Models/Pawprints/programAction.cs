using openshock2coyote.Config.Actions;

namespace openshock2coyote.Models.Pawprints;

public class ProgramAction(BaseAction action)
{
    public Boolean Active = false;
    public BaseAction Action = action;
}