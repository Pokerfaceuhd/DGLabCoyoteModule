namespace openshock2coyote.Utils.PawprintMessages.ReadMessages;

public class TriggerActiveMessage(byte[] message) : BaseReadMessage(message[1])
{
    public readonly byte EventId = message[2];
    public readonly byte ParameterValue = message[3];
}