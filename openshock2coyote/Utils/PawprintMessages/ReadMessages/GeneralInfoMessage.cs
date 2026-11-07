namespace openshock2coyote.Utils.PawprintMessages.ReadMessages;

public class GeneralInfoMessage(byte[] message) : BaseReadMessage(message[1])
{
    public readonly byte DeviceType = message[2];
    public readonly byte BatteryLevel = message[3];
}