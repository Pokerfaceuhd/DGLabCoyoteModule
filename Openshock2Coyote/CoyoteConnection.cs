using System.Collections.Concurrent;
using System.Threading.Channels;
using openshock2coyote.Services;
using InTheHand.Bluetooth;
using LucHeart.WebsocketLibrary;
using LucHeart.WebsocketLibrary.Updatables;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Api;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.MinimalEvents;
using openshock2coyote.Config;
using openshock2coyote.Models.Coyote;
using openshock2coyote.Utils;
using static System.String;
using Channel = openshock2coyote.Models.Coyote.Channel;

namespace openshock2coyote;

public class CoyoteConnection
{
    private readonly ILogger<CoyoteConnection> _logger;
    private IModuleConfig<Openshock2CoyoteConfig> _config;
    private BluetoothDevice? _device;
    private readonly String _deviceId;
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _currentCts;
    private CancellationTokenSource _linkedCts;

    private static readonly BluetoothUuid WaveformServiceId = BluetoothUuid.FromShortId(0x180C);
    private static readonly BluetoothUuid WaveformReadCharacteristicId = BluetoothUuid.FromShortId(0x150B);
    private static readonly BluetoothUuid WaveformWriteCharacteristicId = BluetoothUuid.FromShortId(0x150A);

    private static readonly BluetoothUuid BatteryLevelServiceId = BluetoothUuid.FromShortId(0x180A);
    private static readonly BluetoothUuid BatteryLevelCharacteristicId = BluetoothUuid.FromShortId(0x1500);
    
    private GattCharacteristic? _waveformWriteCharacteristic;
    private GattCharacteristic? _batteryCharacteristic;
    
    private byte _number;
    private byte _cStrengthA = 100;
    private byte _cStrengthB = 100;
    
    public IAsyncMinimalEventObservable OnClose => _onClose;
    private readonly AsyncMinimalEvent _onClose = new();

    private const int TimeMsBetweenPackets = 100;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(TimeMsBetweenPackets));
    
    private readonly ConcurrentQueue<SingleChannelWaveformSeries> _incomingWaveformPackets = new();
    private readonly List<SingleChannelWaveformSeries> _waveformPacketQueue = new List<SingleChannelWaveformSeries>();
    
    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _state =
        new(WebsocketConnectionState.NotStarted);
    public IAsyncUpdatable<WebsocketConnectionState> State => _state;
    
    private readonly AsyncUpdatableVariable<byte> _batteryLevel = new(0);
    public IAsyncUpdatable<byte> BatteryLevel => _batteryLevel;

    private byte[] _lastBFDirectiveCommand = new byte[7];
    
    public CoyoteConnection(
        ILogger<CoyoteConnection> logger,
        IModuleConfig<Openshock2CoyoteConfig> config,
        String deviceId)
    {
        _logger = logger;
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _config = config;
        
        _deviceId = deviceId;
    }

    public async Task OpenAsync()
    {
        _batteryLevel.Value = 0;
        _state.Value = WebsocketConnectionState.Connecting;
        _logger.LogDebug($"Opening connection to coyote {_deviceId}");
        _device = await BluetoothDevice.FromIdAsync(_deviceId);

        if (_device == null)
        {
            _logger.LogError("Coyote device could not be found");
            _state.Value = WebsocketConnectionState.Disconnected;
            _batteryLevel.Value = 0;
            throw new NullReferenceException("Device not found");
        }

        if (_currentCts != null) await _currentCts.CancelAsync();
        _linkedCts.Dispose();
        _currentCts?.Dispose();

        _currentCts = new CancellationTokenSource();
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, _currentCts.Token);

        _logger.LogInformation("Pairing with device: {DeviceName}", _device.Name);
        await _device.Gatt.ConnectAsync();
        if (!_device.Gatt.IsConnected)
        {
            _logger.LogError("Pairing unsuccessful");
            _batteryLevel.Value = 0;
            _state.Value = WebsocketConnectionState.Disconnected;
            return;
        }
        _state.Value = WebsocketConnectionState.Connected;

        var waveformService = await _device.Gatt.GetPrimaryServiceAsync(WaveformServiceId);
        var batteryService = await _device.Gatt.GetPrimaryServiceAsync(BatteryLevelServiceId);

        _batteryCharacteristic = await batteryService.GetCharacteristicAsync(BatteryLevelCharacteristicId);
        _waveformWriteCharacteristic = await waveformService.GetCharacteristicAsync(WaveformWriteCharacteristicId);

        _batteryCharacteristic.CharacteristicValueChanged += UpdateBattery;
        _batteryLevel.Value = (await _batteryCharacteristic.ReadValueAsync())[0];

        _ = OsTask.Run(WriteLoop);
    }

    private void UpdateBattery(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        if (e.Value != null) _batteryLevel.Value = e.Value[0];
    }
    
    private async Task WriteLoop()
    {
        _incomingWaveformPackets.Clear();
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                var bfDirectiveCommand = BfDirectiveBuilder.Build(_config.Config.CoyoteConfig);
                if (!bfDirectiveCommand.SequenceEqual(_lastBFDirectiveCommand))
                {
                    await SendCommand(bfDirectiveCommand);
                    _lastBFDirectiveCommand = bfDirectiveCommand;
                }
                
                while (_incomingWaveformPackets.TryDequeue(out var waveformPacket))
                    _waveformPacketQueue.Add(waveformPacket);

                _waveformPacketQueue.RemoveAll(ps => ps.ChannelWaveforms.Count == 0);
                var currentTickWaveforms = _waveformPacketQueue.Select(ps => ps.ChannelWaveforms.Dequeue());

                var frequencyMs = _config.Config.CoyoteConfig.DutyCycle;
                byte frequencyHz = frequencyMs switch
                {
                    >= 10 and <= 100 => (byte)frequencyMs,
                    >= 101 and <= 600 => (byte)((frequencyMs - 100) / 5 + 100),
                    >= 601 and <= 1000 => (byte)((frequencyMs - 600) / 10 + 200),
                    _ => 10
                };

                WaveformBuilder waveformBuilder = new(frequencyHz, _cStrengthA, _cStrengthB);
                foreach (var waveform in currentTickWaveforms)
                {
                    waveformBuilder.AddChannelWaveform(waveform);
                }

                _cStrengthA = waveformBuilder.StrengthA;
                _cStrengthB = waveformBuilder.StrengthB;

                var waveformCommand = waveformBuilder.ConvertToCommand(_number);
                
                await SendCommand(waveformCommand);

                _number++;
                if (_number > 0b1111)
                {
                    _number = 1;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogTrace("WriteLoop cancelled");
        }
        catch (InvalidOperationException)
        {
            _logger.LogTrace("Coyote disconnected");
            _batteryLevel.Value = 0;
            _state.Value = WebsocketConnectionState.Disconnected;
        }
        
        _logger.LogDebug("WriteLoop cancelled");
    }

    public Task Control(SingleChannelWaveformSeries waveformPacket)
    {
        _logger.LogInformation("Channel: {Channel}, Duration: {Duration}ms, Intensity: {Intensity}", waveformPacket.Channel, waveformPacket.Duration, waveformPacket.Intensity);
        _incomingWaveformPackets.Enqueue(waveformPacket);
        return Task.CompletedTask;
    }

    public void StopCommand(Channel channel)
    {
        _logger.LogInformation("Stopping Channel: {Channel}", channel);
        _waveformPacketQueue.RemoveAll(waveform => waveform.Channel == channel);
    }

    private async Task SendCommand(byte[] command)
    {
        await _waveformWriteCharacteristic!.WriteValueWithoutResponseAsync(command);
    }

    public async Task Close()
    {
        _logger.LogDebug("Closing Coyote connection");
        _device?.Gatt.Disconnect();
        await _onClose.InvokeAsyncParallel();
    }
    
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await Close();
        } catch (Exception e)
        {
            _logger.LogError(e, "Error during DisposeAsync, Calling Close failed");
        }
        _device?.Gatt.Disconnect();
        
        if (_currentCts != null) await _currentCts.CancelAsync();
        await _disposeCts.CancelAsync();
        
        _linkedCts.Dispose();
        _currentCts?.Dispose();
        _disposeCts.Dispose();
    }
}