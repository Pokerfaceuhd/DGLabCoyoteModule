using OpenShock.Serialization.Gateway;

namespace DGLabCoyote.Models.Coyote;

public struct SingleChannelWaveformSeries
{
    public readonly Queue<SingleChannelWaveform> ChannelWaveforms =  new Queue<SingleChannelWaveform>();
    
    private static readonly int segmentLengthMs = 25;
    private static readonly int segmentsPerPacket = 4;
    private static readonly int totalPacketLengthMs = 100;

    public SingleChannelWaveformSeries(Channel channel, ushort duration, byte intensity)
    {
        for (var i = 0; i < duration; i += totalPacketLengthMs)
        {
            var durationLeft = duration - i;
            if (durationLeft < totalPacketLengthMs)
            {
                var segmentCount = Convert.ToByte((durationLeft + segmentLengthMs - 1) / segmentLengthMs);
                ChannelWaveforms.Enqueue(new SingleChannelWaveform(intensity, channel, segmentCount));
            }
            else
            {
                ChannelWaveforms.Enqueue(new SingleChannelWaveform(intensity, channel));
            }
        }


    }
}