using openshock2coyote.Config;
using openshock2coyote.Config.Triggers;

namespace openshock2coyote.Utils.PawprintMessages.WriteMessages;

public class TriggerMessage
{
    private static byte Header = 0x50;
    
    public static byte[] Build(Pawprint pawprint)
    {
        var message = new byte[17];
        message[0] = Header;
        message[1] = pawprint.color;
        message[2] = pawprint.Trigger.TriggerMode;

        switch (pawprint.Trigger.TriggerMode)
        {
            case 0:
                return message;
            case 0xD0:
                message[3] = 0x01;
                return message;
            default:
                switch (pawprint.Trigger)
                {
                    case RandomReactionTrigger random:
                        message[3] = random.TriggerEventId;
                        message[4] = (byte)(random.RandomGreenLightDelaySeconds.Min >> 8);
                        message[5] = (byte)(random.RandomGreenLightDelaySeconds.Min & 0xFF);
                        message[6] = (byte)(random.RandomGreenLightDelaySeconds.Max >> 8);
                        message[7] = (byte)(random.RandomGreenLightDelaySeconds.Max & 0xFF);
                        message[8] = (byte)(random.ReactionTimeSeconds >> 8);
                        message[9] = (byte)(random.ReactionTimeSeconds & 0xFF);
                        message[10] = random.ParameterIncreaseRateWhileActive;
                        message[11] = random.ParameterDecreaseOnDeactivation;
                        message[12] = random.ContinuousParameterDecreaseRateAfterDeactivation;
                        message[13] = random.ParameterIncreaseOnActivation; 
                        break;

                    case ExternalVoltageDetectionTrigger external:
                        message[3] = external.TriggerEventId;
                        message[4] = (byte)external.InternalPullUp;
                        message[5] = external.TargetVoltageRange.Min;
                        message[6] = external.TargetVoltageRange.Max;
                        message[7] = external.ParameterMappingRange;
                        break;

                    case PressReleaseTriggerAndAccelerationAngleTrigger button:
                        message[3] = button.PressReleaseTriggerEventId;
                        message[4] = PackPressReleaseSettings(button.PressReleaseMode, 
                            button.AccelerationOrAngleSubmode, 
                            button.ActivationDebounce, 
                            button.DeactivationDebounce);
                        message[5] = button.PressReleaseParameterIncreaseRateWhileActive;
                        message[6] = button.PressReleaseParameterDecreaseOnDeactivation;
                        message[7] = button.PressReleaseParameterDecreaseRateAfterDeactivation;
                        message[8] = button.PressReleaseParameterIncreaseOnActivation;
                        message[9] = button.AccelerationAngleTriggerEventId;

                        if (button.AccelerationOrAngleSubmode == AccelerationAngleSubmode.Acceleration)
                        {
                            message[10] = (byte)button.AccelerationDetectionMode;
                            message[11] = (byte)(button.AccelerationThreshold >> 8);
                            message[12] = (byte)(button.AccelerationThreshold & 0xFF);
                        }
                        else
                        {
                            message[10] = unchecked((byte)button.RangeX.Min);
                            message[11] = unchecked((byte)button.RangeX.Max);
                            message[12] = unchecked((byte)button.RangeY.Min);
                            message[13] = unchecked((byte)button.RangeY.Max);
                            message[14] = unchecked((byte)button.RangeZ.Min);
                            message[15] = unchecked((byte)button.RangeZ.Max);
                        }
                        message[16] = button.AccelerationAngleParameterMappingRange;
                        break;

                    case ProbabilityTrigger probability:
                        for (var i = 0; i < probability.TriggerIds.Length; i++)
                        {
                            message[i*2+3] = probability.TriggerIds[i];
                            message[i*2+4] = probability.TriggerProbabilities[i];
                        }
                        message[15] = (byte)(probability.CooldownSeconds >> 8);
                        message[16] = (byte)(probability.CooldownSeconds & 0xFF);
                        break;
                }
        
        
                return message;
        }
    }

    private static byte PackPressReleaseSettings(
        PressReleaseTriggerMode mode,
        AccelerationAngleSubmode submode,
        byte activationDebounce,
        byte deactivationDebounce)
        => (byte)(
            ((byte)mode & 0b1) |
            (((byte)submode & 0b1) << 1) |
            ((activationDebounce & 0b111) << 2) |
            ((deactivationDebounce & 0b111) << 5));
}