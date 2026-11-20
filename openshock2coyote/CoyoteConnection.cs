using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using LucHeart.WebsocketLibrary;
using LucHeart.WebsocketLibrary.Updatables;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.MinimalEvents;
using openshock2coyote.Config;
using openshock2coyote.Models.Coyote;
using openshock2coyote.Utils;
using Shiny.BluetoothLE;
using static System.String;
using Channel = openshock2coyote.Models.Coyote.Channel;

namespace openshock2coyote;

public class CoyoteConnection
{
    private readonly ILogger<CoyoteConnection> _logger;
    private IModuleConfig<Openshock2CoyoteConfig> _config;
    
    private IPeripheral? _peripheral;
    private readonly string _deviceId;
    
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _currentCts;
    private IDisposable? _disconnectSubscription;

    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private CancellationTokenSource? _connectionCts;
    
    private const string WaveformServiceId = "0000180c-0000-1000-8000-00805f9b34fb";
    private const string WaveformNotifyCharacteristicId = "0000150b-0000-1000-8000-00805f9b34fb";
    private const string WaveformWriteCharacteristicId = "0000150a-0000-1000-8000-00805f9b34fb";
    
    private const string BatteryServiceId = "0000180a-0000-1000-8000-00805f9b34fb";
    private const string BatteryCharacteristicId = "00001500-0000-1000-8000-00805f9b34fb";
    
    private IDisposable? _batteryNotifyCharacteristic;
    private IDisposable? _waveformNotifyCharacteristic;

    private byte _number;

    public IAsyncMinimalEventObservable OnClose => _onClose;
    private readonly AsyncMinimalEvent _onClose = new();

    private const int TimeMsBetweenPackets = 100;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(TimeMsBetweenPackets));

    private readonly ConcurrentQueue<SingleChannelWaveformSeries> _incomingWaveformPackets = new();
    private readonly List<SingleChannelWaveformSeries> _waveformPacketQueue = [];

    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _state =
        new(WebsocketConnectionState.Disconnected);
    public IAsyncUpdatable<WebsocketConnectionState> State => _state;

    private readonly AsyncUpdatableVariable<byte> _battery = new(0);
    public IAsyncUpdatable<byte> Battery => _battery;

    private byte[] _lastBfDirectiveCommand = new byte[7];
    private readonly Timer? _configTimer;
    private readonly SemaphoreSlim _configLock = new(1, 1);


    public CoyoteConnection(
        ILogger<CoyoteConnection> logger,
        IModuleConfig<Openshock2CoyoteConfig> config)
    {
        _logger = logger;
        _config = config;
    }
    
    public async Task OpenAsync(IPeripheral peripheral)
    {
        _state.Value = WebsocketConnectionState.Connecting;
        ArgumentNullException.ThrowIfNull(peripheral);

        await _connectionLock.WaitAsync(_disposeCts.Token);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await DisconnectCoreAsync();

            _connectionCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _disposeCts.Token);

            var cancellationToken = _connectionCts.Token;

            _peripheral = peripheral;
            _battery.Value = 0;

            _logger.LogInformation(
                "Connecting to Coyote: {DeviceName} ({DeviceId})",
                peripheral.Name,
                peripheral.Uuid);
            
            await peripheral.ConnectAsync(new ConnectionConfig(AutoConnect: false), cancellationToken);
            
            _logger.LogInformation(
                "Status: {Status}",
                peripheral.Status);
            
            _logger.LogInformation(
                "Coyote to Pawprint: {DeviceName}",
                peripheral.Name);
            
            _disconnectSubscription = peripheral
                .WhenDisconnected()
                .Subscribe(
                    _ => OnDisconnected(peripheral),
                    ex => _logger.LogWarning(
                        ex,
                        "Coyote disconnect observer failed"));

            var services = await peripheral.GetServicesAsync(cancelToken: cancellationToken);

            foreach (var service in services)
            {
                _logger.LogInformation("Service found: {ServiceUuid}" ,service.Uuid);
            }
                
            // Verify that the Pawprint service is available.
            await FindService(peripheral, BatteryServiceId, cancellationToken);
            await FindService(peripheral, WaveformServiceId, cancellationToken);
            
            _batteryNotifyCharacteristic = peripheral
                .NotifyCharacteristic(
                    BatteryServiceId,
                    BatteryCharacteristicId)
                .Subscribe(
                    result => UpdateBattery(result.Data),
                    ex =>
                    {
                        _logger.LogError(
                            ex,
                            "Pawprint notification stream failed");

                        OnDisconnected(peripheral);
                    });
            
            _state.Value = WebsocketConnectionState.Connected;
            _ = OsTask.Run(WriteLoop, cancellationToken);
        }
        catch
        {
            await DisconnectCoreAsync();
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static async Task FindService(IPeripheral peripheral, string serviceId, CancellationToken cancellationToken)
    {
        var service = await peripheral.GetServiceAsync(serviceId, cancelToken: cancellationToken);

        if (service is null)
            throw new InvalidOperationException(
                $"Coyote GATT service {serviceId} not found.");
    }
    
    private void OnDisconnected(IPeripheral peripheral)
    {
        if (!ReferenceEquals(_peripheral, peripheral))
            return;

        _logger.LogInformation(
            "Coyote disconnected: {DeviceName}",
            peripheral.Name);

        _batteryNotifyCharacteristic?.Dispose();
        _batteryNotifyCharacteristic = null;
        
        _waveformNotifyCharacteristic?.Dispose();
        _waveformNotifyCharacteristic = null;
        
        _state.Value = WebsocketConnectionState.Disconnected;
        _battery.Value = 0;
    }
    
    private async Task DisconnectCoreAsync()
    {
        _state.Value = WebsocketConnectionState.Disconnected;
        
        if (_connectionCts != null)
        {
            await _connectionCts.CancelAsync();
            _connectionCts.Dispose();
            _connectionCts = null;
        }

        _batteryNotifyCharacteristic?.Dispose();
        _batteryNotifyCharacteristic = null;
        
        _waveformNotifyCharacteristic?.Dispose();
        _waveformNotifyCharacteristic = null;
        
        if (_peripheral != null)
        {
            try
            {
                // Also cancels any pending automatic reconnect.
                _peripheral.CancelConnection();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting Pawprint");
            }

            _peripheral = null;
        }
        
        await _onClose.InvokeAsyncParallel();
        _state.Value = WebsocketConnectionState.Disconnected;
    }
    /*
    public async Task OpenAsync()
    {
        _battery.Value = 0;
        _state.Value = WebsocketConnectionState.Connecting;
        _logger.LogDebug($"Opening connection to coyote {_deviceId}");
        _device = await BluetoothDevice.FromIdAsync(_deviceId);

        if (_device == null)
        {
            _logger.LogError("Coyote device could not be found");
            _state.Value = WebsocketConnectionState.Disconnected;
            _battery.Value = 0;
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
            _battery.Value = 0;
            _state.Value = WebsocketConnectionState.Disconnected;
            return;
        }
        _logger.LogInformation("Pairing done, connecting Services: {DeviceName}", _device.Name);
        _state.Value = WebsocketConnectionState.Connected;

        var waveformService = await _device.Gatt.GetPrimaryServiceAsync(WaveformServiceId);
        var batteryService = await _device.Gatt.GetPrimaryServiceAsync(BatteryServiceId);

        _batteryNotifyCharacteristic = await batteryService.GetCharacteristicAsync(BatteryCharacteristicId);
        _waveformWriteCharacteristic = await waveformService.GetCharacteristicAsync(WaveformWriteCharacteristicId);
        _waveformNotifyCharacteristic = await waveformService.GetCharacteristicAsync(WaveformNotifyCharacteristicId);

        _batteryNotifyCharacteristic.CharacteristicValueChanged += UpdateBattery;
        _battery.Value = (await _batteryNotifyCharacteristic.ReadValueAsync())[0];
        await _batteryNotifyCharacteristic.StartNotificationsAsync();

        _waveformNotifyCharacteristic.CharacteristicValueChanged += NotifyUpdate;
        //await _waveformNotifyCharacteristic.StartNotificationsAsync();

        _ = OsTask.Run(WriteLoop);
    }
    */

    private void UpdateBattery(byte[]? batteryValue)
    {
        if (batteryValue != null) _battery.Value = batteryValue[0];
    }

    private async Task WriteLoop()
    {
        _logger.LogInformation("Starting Writeloop");
        _incomingWaveformPackets.Clear();
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
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

                WaveformBuilder waveformBuilder = new(frequencyHz, 0, 0);
                foreach (var waveform in currentTickWaveforms)
                {
                    waveformBuilder.AddChannelWaveform(waveform);
                }

                var waveformCommand = waveformBuilder.ConvertToCommand(_number);

                if (waveformCommand[8] > 0)
                {
                    //_logger.LogInformation("Send new waveform {waveform}", Join(";",waveformCommand));
                } 
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
            _battery.Value = 0;
            _state.Value = WebsocketConnectionState.Disconnected;
        }

        _logger.LogDebug("WriteLoop cancelled");
    }

    public Task Control(SingleChannelWaveformSeries waveformPacket)
    {
        _incomingWaveformPackets.Enqueue(waveformPacket);
        return Task.CompletedTask;
    }
    
    public void UpdateCoyoteSettings()
        => _configTimer?.Change(TimeSpan.FromMilliseconds(1000), Timeout.InfiniteTimeSpan);
    
    private async Task UpdateConfig()
    {
        await _configLock.WaitAsync();
        try
        {
            if(_state.Value ==  WebsocketConnectionState.Connected)
                await SendCommand(BfDirectiveBuilder.Build(_config.Config.CoyoteConfig));
        }
        catch (InvalidOperationException) { }
        finally
        {
            _configLock.Release();
        }
    }

    public void StopCommand(Channel channel)
    {
        _logger.LogInformation("Stopping Channel: {Channel}", channel);
        _waveformPacketQueue.RemoveAll(waveform => waveform.Channel == channel);
    }

    private async Task SendCommand(byte[] command)
    {
        var peripheral = _peripheral
                         ?? throw new InvalidOperationException("Pawprint is not connected.");

        var cancellationToken = _connectionCts?.Token
                                ?? throw new InvalidOperationException("No active connection.");

        await peripheral
            .WriteCharacteristic(
                WaveformServiceId,
                WaveformWriteCharacteristicId,
                command,
                withResponse: false)
            .ToTask(cancellationToken);
    }

    public async Task Disconnect()
    {
        if (_peripheral != null) await _peripheral.DisconnectAsync(timeout: new TimeSpan(0, 0, 5));
    }
    
    private async Task Close()
    {
        await _connectionLock.WaitAsync();

        try
        {
            _logger.LogDebug("Closing Pawprint connection");

            await DisconnectCoreAsync();
            await _onClose.InvokeAsyncParallel();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _disposeCts.CancelAsync();
        await Close();

        _disposeCts.Dispose();
        _connectionLock.Dispose();
    }
}
