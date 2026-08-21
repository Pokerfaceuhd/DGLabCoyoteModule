namespace openshock2coyote.Models.Coyote;

public struct SingleChannelWaveformSeries
{
    public readonly Queue<SingleChannelWaveform> ChannelWaveforms = new();
    public Channel Channel;

    private const int SegmentLengthMs = 25;
    private const int TotalPacketLengthMs = 100;

    public SingleChannelWaveformSeries(Channel channel, byte maxStrength, ushort duration, byte intensity)
    {
        for (var i = 0; i < duration; i += TotalPacketLengthMs)
        {
            var durationLeft = duration - i;
            if (durationLeft < TotalPacketLengthMs)
            {
                var segmentCount = Convert.ToByte((durationLeft + SegmentLengthMs - 1) / SegmentLengthMs);
                ChannelWaveforms.Enqueue(new SingleChannelWaveform(intensity, maxStrength, channel, segmentCount));
            }
            else
            {
                ChannelWaveforms.Enqueue(new SingleChannelWaveform(intensity, maxStrength, channel));
            }
        }
    }
}
