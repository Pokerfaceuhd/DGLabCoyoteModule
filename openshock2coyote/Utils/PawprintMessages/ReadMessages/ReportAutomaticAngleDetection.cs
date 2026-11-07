using System.Buffers.Binary;
using openshock2coyote.Config;

namespace openshock2coyote.Utils.PawprintMessages.ReadMessages;

public class ReportAutomaticAngleDetection(byte[] message, byte color)
{
    public readonly byte Color = color;
    public readonly JsonRange<sbyte> RangeX = new()
    {
        Min = (sbyte)Math.Clamp(
            BinaryPrimitives.ReadInt16LittleEndian(message[2..4]) / 2,
            sbyte.MinValue,
            sbyte.MaxValue),
        Max = (sbyte)Math.Clamp(
            BinaryPrimitives.ReadInt16LittleEndian(message[4..6]) / 2,
            sbyte.MinValue,
            sbyte.MaxValue)
    };

    public readonly JsonRange<sbyte> RangeY = new()
    {
        Min = (sbyte)Math.Clamp(
            BinaryPrimitives.ReadInt16LittleEndian(message[6..8]) / 2,
            sbyte.MinValue,
            sbyte.MaxValue),
        Max = (sbyte)Math.Clamp(
            BinaryPrimitives.ReadInt16LittleEndian(message[8..10]) / 2,
            sbyte.MinValue,
            sbyte.MaxValue)
    };

    public readonly JsonRange<sbyte> RangeZ = new()
    {
        Min = (sbyte)Math.Clamp(
            BinaryPrimitives.ReadInt16LittleEndian(message[10..12]) / 2,
            sbyte.MinValue,
            sbyte.MaxValue),
        Max = (sbyte)Math.Clamp(
            BinaryPrimitives.ReadInt16LittleEndian(message[12..14]) / 2,
            sbyte.MinValue,
            sbyte.MaxValue)
    };
}