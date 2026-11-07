namespace openshock2coyote.Utils.PawprintMessages.ReadMessages;

public class TriggerDeactivatedMessage(byte[] message) : BaseReadMessage(message[1])
{
    public readonly byte EventId = message[2];
}