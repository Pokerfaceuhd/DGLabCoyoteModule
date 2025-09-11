using System.Collections.Concurrent;
using System.Threading.Channels;
using DGLabCoyote.Config;
using DGLabCoyote.Models.Coyote;
using DGLabCoyote.Services;
using DGLabCoyote.Utils;
using InTheHand.Bluetooth;
using LucHeart.WebsocketLibrary;
using LucHeart.WebsocketLibrary.Updatables;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Api;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.MinimalEvents;

namespace DGLabCoyote;

public class CoyoteConnection
{
    private readonly ILogger<CoyoteConnection> _logger;
    private IModuleConfig<DgLabCoyoteConfig> _config;
    private BluetoothDevice? _device;
    private readonly String _deviceId;
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _currentCts;
    private CancellationTokenSource _linkedCts;

    private static readonly BluetoothUuid WaveformServiceId = BluetoothUuid.FromShortId(0x180C);
    private static readonly BluetoothUuid WaveformReadCharacteristicId = BluetoothUuid.FromShortId(0x150B);
    private static readonly BluetoothUuid WaveformWriteCharacteristicId = BluetoothUuid.FromShortId(0x150A);

    public static readonly int TimeMsBetweenPackets = 100;
    private byte _number = 0;
    private byte _cStrengthA = 0;
    private byte _cStrengthB = 0;
    
    private SemaphoreSlim _packetSemaphore = new SemaphoreSlim(0, 1);
    
    private GattCharacteristic? _waveformWriteCharacteristic;
    
    public IAsyncMinimalEventObservable OnClose => _onClose;
    private readonly AsyncMinimalEvent _onClose = new();
    
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(TimeMsBetweenPackets));
    
    private readonly ConcurrentQueue<SingleChannelWaveformSeries> _incomingWaveformPackets = new();
    private readonly List<SingleChannelWaveformSeries> _waveformPacketQueue = new List<SingleChannelWaveformSeries>();
    
    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _state =
        new(WebsocketConnectionState.NotStarted);

    public IAsyncUpdatable<WebsocketConnectionState> State => _state;
    
    public CoyoteConnection(
        ILogger<CoyoteConnection> logger,
        IModuleConfig<DgLabCoyoteConfig> config,
        String deviceId)
    {
        _logger = logger;
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _config = config;
        
        _deviceId = deviceId;
    }

    public async Task OpenAsync()
    {
        _state.Value = WebsocketConnectionState.Connecting;
        _logger.LogDebug($"Opening connection to coyote {_deviceId}");
        _device = await BluetoothDevice.FromIdAsync(_deviceId);

        if (_device == null)
        {
            _logger.LogError("Coyote device could not be found");
            throw new NullReferenceException("Device not found");
        }
        
        if (_currentCts != null) await _currentCts.CancelAsync();
        _linkedCts.Dispose();
        _currentCts?.Dispose();
        
        _currentCts = new CancellationTokenSource();
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, _currentCts.Token);
        
        _logger.LogInformation("Pairing with device: {DeviceName}", _device.Name);
        await _device.Gatt.ConnectAsync();
        _state.Value = WebsocketConnectionState.Connected;
        
        _device.GattServerDisconnected += GattServerDisconnected;

        var waveformService = await _device.Gatt.GetPrimaryServiceAsync(WaveformServiceId);
        
        _waveformWriteCharacteristic = await waveformService.GetCharacteristicAsync(WaveformWriteCharacteristicId);
        var waveformReadCharacteristic = await waveformService.GetCharacteristicAsync(WaveformReadCharacteristicId);
        
        waveformReadCharacteristic.CharacteristicValueChanged += ReadBack;
        await waveformReadCharacteristic.StartNotificationsAsync();
        
        OsTask.Run(WriteLoop);
    }

    private void GattServerDisconnected(object? sender, EventArgs e)
    {
        _state.Value = WebsocketConnectionState.Disconnected;
        _ = OpenAsync();
    }

    private void ReadBack(object? sender, GattCharacteristicValueChangedEventArgs e)
    {
        var receivedNumber = e.Value[1];
        if (_number != receivedNumber)
        {
            if (e.Value.Length == 6)
            {
                _packetSemaphore.Release();
            }
        }
        else
        {
            _packetSemaphore.Release();
        }
    }

    private async Task WriteLoop()
    {
        _incomingWaveformPackets.Clear();
        DateTimeOffset time = DateTimeOffset.Now;
        int countPerSecond = 0;
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                while (_incomingWaveformPackets.TryDequeue(out var waveformPacket))
                    _waveformPacketQueue.Add(waveformPacket);
                
                _waveformPacketQueue.RemoveAll(ps => ps.ChannelWaveforms.Count == 0);
                var currentTickWaveforms = _waveformPacketQueue.Select(ps => ps.ChannelWaveforms.Dequeue());
                
                WaveformBuilder waveformBuilder = new(_cStrengthA, _cStrengthB, _config.Config.BluetoothConnection.Frequency);
                foreach (var waveform in currentTickWaveforms)
                {
                    waveformBuilder.AddChannelWaveform(waveform);
                }

                _cStrengthA = waveformBuilder._strengthA;
                _cStrengthB = waveformBuilder._strengthB;
                
                var command = waveformBuilder.ConvertToCommand(_number);
                
                await _waveformWriteCharacteristic!.WriteValueWithoutResponseAsync(command);
                
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
        
        _logger.LogDebug("WriteLoop cancelled");
    }

    public async Task Control(SingleChannelWaveformSeries waveformPacket)
    {
        _incomingWaveformPackets.Enqueue(waveformPacket);
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