namespace openshock2coyote.Utils.PawprintMessages.ReadMessages;

public class ParameterChangeMessage(byte[] message) : BaseReadMessage(message[1])
{
    public readonly byte EventId = message[2];
    public readonly byte ParamaterValue = message[3];
}