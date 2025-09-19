namespace openshock2coyote.Models.Coyote;

public struct SingleChannelWaveform
{
    public readonly Channel Channel;
    
    public readonly byte Strength;

    public readonly byte[] Intensity;
    
    public SingleChannelWaveform(byte strength, byte[] intensity, Channel channel)
    {
        Strength = 100;
        Intensity = intensity;
        Channel = channel;
    }

    public SingleChannelWaveform(byte strength, Channel channel, byte amountOfSegments)
    {
        byte[] intensityArray = new byte[4];
        for (byte i = 0; i < amountOfSegments; i++)
            intensityArray[i] = strength;
        
        Strength = 100;
        Intensity = intensityArray;
        Channel = channel;
    }
    
    public SingleChannelWaveform(byte strength, Channel channel)
    {
        Strength = 100;
        Intensity = [strength,strength,strength,strength];
        Channel = channel;
    }
}

public enum Channel
{
    A = 0,
    B = 1
}