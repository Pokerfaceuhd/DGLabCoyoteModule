using System.ComponentModel.DataAnnotations;
using openshock2coyote.Config;

namespace openshock2coyote.Utils;

public static class BfDirectiveBuilder
{
    private const byte Head = 0xBF;

    public static byte[] Build(CoyoteConfig config)
    {
        var data = new byte[7];

        BFDirectiveConfig bfDirective = config.BfDirective;
        
        var maxStrength = (byte)Math.Max(config.ShockMultiplierRange.Max*100, config.VibrateMultiplierRange.Max*100);

        data[1] = maxStrength;
        data[2] = maxStrength;
        data[3] = bfDirective.AFrequencyBalance;
        data[4] = bfDirective.BFrequencyBalance;
        data[5] = bfDirective.APulseWidth;
        data[6] = bfDirective.BPulseWidth;
        
        return data;
    }
}