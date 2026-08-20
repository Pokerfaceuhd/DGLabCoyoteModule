using System.ComponentModel.DataAnnotations;
using openshock2coyote.Config;

namespace openshock2coyote.Utils;

public static class BfDirectiveBuilder
{
    private const byte Head = 0xBF;

    public static byte[] Build(CoyoteConfig config)
    {
        byte[] data = new byte[7];

        BFDirectiveConfig bfDirective = config.BfDirective;

        byte maxStrength = (byte)Math.Clamp(Math.Max(config.ShockMultiplierRange.Max, config.VibrateMultiplierRange.Max) * 100, 0, 200);

        data[0] = Head;
        data[1] = maxStrength;
        data[2] = maxStrength;
        data[3] = bfDirective.AFrequencyBalance;
        data[4] = bfDirective.BFrequencyBalance;
        data[5] = bfDirective.APulseWidth;
        data[6] = bfDirective.BPulseWidth;

        return data;
    }
}
