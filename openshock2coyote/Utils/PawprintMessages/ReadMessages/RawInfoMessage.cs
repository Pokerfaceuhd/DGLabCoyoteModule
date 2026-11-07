namespace openshock2coyote.Utils.PawprintMessages.ReadMessages;

public class RawInfoMessage(byte[] message) : BaseReadMessage(message[1])
{
    public readonly byte SequenceNumber = message[2];
    public readonly byte ButtonState = message[3];
    public readonly byte Acceleration = message[4];
    public readonly byte XAngle = message[5];
    public readonly byte YAngle = message[6];
    public readonly byte ZAngle = message[7];
    public readonly byte ExternalVoltage = message[8];
}