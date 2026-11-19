using System.Buffers.Binary;
using openshock2coyote.Config;

namespace openshock2coyote.Utils.PawprintMessages.ReadMessages;

public class ReportAutomaticAngleDetection(byte[] message, byte color)
{
    public readonly byte Color = color;

    private static sbyte ReadThreshold(byte[] message, int offset)
        => (sbyte)Math.Clamp(
            BinaryPrimitives.ReadInt16BigEndian(message.AsSpan(offset, 2)) / 2,
            sbyte.MinValue,
            sbyte.MaxValue);

    public readonly JsonRange<sbyte> RangeX = new()
    {
        Min = ReadThreshold(message, 2),
        Max = ReadThreshold(message, 4)
    };

    public readonly JsonRange<sbyte> RangeY = new()
    {
        Min = ReadThreshold(message, 6),
        Max = ReadThreshold(message, 8)
    };

    public readonly JsonRange<sbyte> RangeZ = new()
    {
        Min = ReadThreshold(message, 10),
        Max = ReadThreshold(message, 12)
    };
}