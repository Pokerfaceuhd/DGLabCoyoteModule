using OpenShock.Serialization.Gateway;

namespace DGLabCoyote.Models.Coyote;

public struct SingleChannelWaveformSeries
{
    public readonly Queue<SingleChannelWaveform> ChannelWaveforms =  new Queue<SingleChannelWaveform>();

    private const int SegmentLengthMs = 25;
    private const int TotalPacketLengthMs = 100;

    public SingleChannelWaveformSeries(Channel channel, ushort duration, byte intensity)
    {
        for (var i = 0; i < duration; i += TotalPacketLengthMs)
        {
            var durationLeft = duration - i;
            if (durationLeft < TotalPacketLengthMs)
            {
                var segmentCount = Convert.ToByte((durationLeft + SegmentLengthMs - 1) / SegmentLengthMs);
                ChannelWaveforms.Enqueue(new SingleChannelWaveform(intensity, channel, segmentCount));
            }
            else
            {
                ChannelWaveforms.Enqueue(new SingleChannelWaveform(intensity, channel));
            }
        }


    }
}