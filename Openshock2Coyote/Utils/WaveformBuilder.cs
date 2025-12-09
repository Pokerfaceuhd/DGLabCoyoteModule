using openshock2coyote.Models.Coyote;

namespace openshock2coyote.Utils;

public class WaveformBuilder
{
    private const byte Head = 0xB0;

    public byte StrengthA;
    
    public byte StrengthB;
    
    private readonly byte[] _frequencyA;
    
    private readonly byte[] _frequencyB;
    
    private readonly byte[] _intensityA;
    
    private readonly byte[] _intensityB;

    private bool _changedStrength = false;
    public WaveformBuilder(byte frequency, byte cStrengthA, byte cStrengthB)
    {
        StrengthA = 0;
        StrengthB = 0;
        _frequencyA = [frequency,frequency,frequency,frequency];
        _frequencyB = [frequency,frequency,frequency,frequency];
        _intensityA = "\0\0\0\0"u8.ToArray();
        _intensityB = "\0\0\0\0"u8.ToArray();
    }

    public void AddChannelWaveform(SingleChannelWaveform singleChannelWaveform)
    {
        if (singleChannelWaveform.Channel == Channel.A)
        {
            for (var i = 0; i < singleChannelWaveform.Intensity.Length; i++)
                _intensityA[i] = Math.Max(_intensityA[i], singleChannelWaveform.Intensity[i]);
            
            if (singleChannelWaveform.Strength <= StrengthA) return;
            StrengthA = singleChannelWaveform.Strength;
        }
        else
        {
            for (var i = 0; i < singleChannelWaveform.Intensity.Length; i++)
                _intensityB[i] = Math.Max(_intensityB[i], singleChannelWaveform.Intensity[i]);
            
            if (singleChannelWaveform.Strength <= StrengthB) return;
            StrengthB = singleChannelWaveform.Strength;
        }
        _changedStrength = true;
    }
    
    public byte[] ConvertToCommand(byte number)
    {
        var data = new byte[20];

        byte strengthInterpretation = 0b0000;
        if (_changedStrength)
        {
            strengthInterpretation = 0b1111;
        }
        
        var numberAndStrengthInterpretation = (byte)(number << 4 | strengthInterpretation);

        data[0] = Head;
        data[1] = numberAndStrengthInterpretation;
        data[2] = StrengthA;
        data[3] = StrengthB;

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