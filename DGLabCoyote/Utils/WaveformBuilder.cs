using DGLabCoyote.Models.Coyote;

namespace DGLabCoyote.Utils;

public class WaveformBuilder
{
    private static byte _head = 0xB0;
    
    private byte _strengthA;
    
    private byte _strengthB;
    
    private byte[] _frequencyA;
    
    private byte[] _frequencyB;
    
    private readonly byte[] _intensityA;
    
    private readonly byte[] _intensityB;

    public WaveformBuilder()
    {
        _strengthA = 0;
        _strengthB = 0;
        _frequencyA = [100,100,100,100];
        _frequencyB = [100,100,100,100];
        _intensityA = [0,0,0,0];
        _intensityB = [0,0,0,0];
    }

    public void AddChannelWaveform(SingleChannelWaveform singleChannelWaveform)
    {
        if (singleChannelWaveform.Channel == Channel.A)
        {
            _strengthA = Math.Max(_strengthA, singleChannelWaveform.Strength);
            for (int i = 0; i < singleChannelWaveform.Intensity.Length; i++)
                _intensityA[i] = Math.Max(_intensityA[i], singleChannelWaveform.Intensity[i]);
        }
        else
        {
            _strengthB = Math.Max(_strengthB, singleChannelWaveform.Strength);
            for (int i = 0; i < singleChannelWaveform.Intensity.Length; i++)
                _intensityB[i] = Math.Max(_intensityB[i], singleChannelWaveform.Intensity[i]);
        }
    }

    public void ChangeFrequency(byte[] frequencyA, byte[] frequencyB)
    {
        _frequencyA = frequencyA;
        _frequencyB = frequencyB;
    }
    
    public byte[] ConvertToCommand(byte number)
    {
        byte[] data = new byte[20];

        byte numberAndStrengthInterpretation = (byte)(number << 4 | 0b1111);

        data[0] = _head;
        data[1] = numberAndStrengthInterpretation;
        data[2] = _strengthA;
        data[3] = _strengthB;

        data[4]  = _frequencyA[0];
        data[5]  = _frequencyA[1];
        data[6]  = _frequencyA[2];
        data[7]  = _frequencyA[3];
        
        data[8]  = _intensityA[0];
        data[9]  = _intensityA[1];
        data[10] = _intensityA[2];
        data[11] = _intensityA[3];

        data[12] = _frequencyB[0];
        data[13] = _frequencyB[1];
        data[14] = _frequencyB[2];
        data[15] = _frequencyB[3];

        data[16] = _intensityB[0];
        data[17] = _intensityB[1];
        data[18] = _intensityB[2];
        data[19] = _intensityB[3];
        
        return data.ToArray();
    }
}