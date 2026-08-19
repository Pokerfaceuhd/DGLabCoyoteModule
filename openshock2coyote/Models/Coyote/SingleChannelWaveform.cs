namespace openshock2coyote.Models.Coyote;

public struct SingleChannelWaveform
{
    public readonly Channel Channel;
    
    public readonly byte Strength;

    public readonly byte[] Intensity;

    public SingleChannelWaveform(byte intensity, byte strength, Channel channel, byte amountOfSegments)
    {
        byte[] intensityArray = new byte[4];
        for (byte i = 0; i < amountOfSegments; i++)
            intensityArray[i] = intensity;
        
        Strength = strength;
        Intensity = intensityArray;
        Channel = channel;
    }
    
    public SingleChannelWaveform(byte intensity, byte strength, Channel channel)
    {
        Strength = strength;
        Intensity = [intensity,intensity,intensity,intensity];
        Channel = channel;
    }
}

public enum Channel
{
    A = 0,
    B = 1
}