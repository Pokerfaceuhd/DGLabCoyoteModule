
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using LucHeart.WebsocketLibrary;
using LucHeart.WebsocketLibrary.Updatables;
using LucHeart.WebsocketLibrary.Utils;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.MinimalEvents;
using Shiny.BluetoothLE;
using openshock2coyote.Config;
using openshock2coyote.Utils;
using openshock2coyote.Utils.PawprintMessages.ReadMessages;
using openshock2coyote.Utils.PawprintMessages.WriteMessages;

namespace openshock2coyote;

public class PawprintConnection : IAsyncDisposable
{
    private readonly ILogger<PawprintConnection> _logger;
    private readonly IModuleConfig<Openshock2CoyoteConfig> _config;
    public readonly byte Color;

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private CancellationTokenSource? _connectionCts;
    private IPeripheral? _peripheral;

    private IDisposable? _notificationSubscription;
    private IDisposable? _disconnectSubscription;

    private bool _disposed;

    public string CurrentlyConnectedDeviceId { get; private set; } = "";
    
    private readonly AsyncUpdatableVariable<byte> _battery =
        new(0);

    public IAsyncUpdatable<byte> Battery => _battery;

    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _state =
        new(WebsocketConnectionState.NotStarted);

    public IAsyncUpdatable<WebsocketConnectionState> State => _state;
    
    private const string ServiceUuid =
        "0000180c-0000-1000-8000-00805f9b34fb";

    private const string WriteCharacteristicUuid =
        "0000150a-0000-1000-8000-00805f9b34fb";

    private const string NotifyCharacteristicUuid =
        "0000150b-0000-1000-8000-00805f9b34fb";
    
    public IAsyncMinimalEventObservable OnClose => _onClose;
    private readonly AsyncMinimalEvent _onClose = new();

    private readonly AsyncUpdatableVariable<RawInfoMessage> _currentRawData = new(new RawInfoMessage([0,0,0,0,0,0,0,0,0]));
    public IAsyncUpdatable<RawInfoMessage?> CurrentRawData => _currentRawData;
    
    public event Func<TriggerActiveMessage, Task>? OnTriggerActivated;
    public event Func<TriggerDeactivatedMessage, Task>? OnTriggerDeactivated;
    public event Func<ParameterChangeMessage, Task>? OnParameterChanged;
    public event Func<ReportAutomaticAngleDetection, Task>? OnAutomaticAngleDetection;
    public event Func<Task>? ConfigUpdated;
    private readonly Timer? _configTimer;
    private readonly SemaphoreSlim _configLock = new(1, 1);
    private bool _receivedStartupMessage;
    
    public PawprintConnection(
        ILogger<PawprintConnection> logger,
        IModuleConfig<Openshock2CoyoteConfig> config,
        byte color)
    {
        _logger = logger;
        _config = config;
        Color = color;
        
        _configTimer = new Timer(_ => OsTask.Run(UpdateConfig),
            null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public async Task OpenAsync(IPeripheral peripheral)
    {
        ArgumentNullException.ThrowIfNull(peripheral);

        await _connectionLock.WaitAsync(_disposeCts.Token);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Already connecting or connected to this device.
            if (_peripheral?.Uuid == peripheral.Uuid &&
                _state.Value is WebsocketConnectionState.Connecting or WebsocketConnectionState.Connected)
            {
                _logger.LogDebug(
                    "Ignoring duplicate connection attempt for {Uuid}",
                    peripheral.Uuid);

                return;
            }

            if (_peripheral != null)
                await DisconnectCoreAsync();

            _connectionCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _disposeCts.Token);

            var cancellationToken = _connectionCts.Token;

            _peripheral = peripheral;
            CurrentlyConnectedDeviceId = peripheral.Uuid;
            _state.Value = WebsocketConnectionState.Connecting;

            //var strength = await peripheral.ReadRssiAsync(cancellationToken, 500);
            
            _logger.LogInformation(
                "Connecting to Pawprint: {DeviceName} ({DeviceId}) Strength {Rssi}dBm",
                peripheral.Name,
                peripheral.Uuid,
                0);

            // PawprintService handles reconnection itself.
            _logger.LogInformation("connecting");
            peripheral.WhenStatusChanged().Subscribe(status =>
            {
                _logger.LogInformation(
                    "Bluetooth status changed: {Status} ({Uuid})",
                    status,
                    peripheral.Uuid
                );
            });
            
            await peripheral.ConnectAsync(new ConnectionConfig(AutoConnect: false), cancellationToken);

            _logger.LogInformation(
                "Connected to Pawprint: {DeviceUuid}",
                peripheral.Uuid);
            
            _disconnectSubscription = peripheral
                .WhenDisconnected()
                .Subscribe(
                    _ => OnDisconnected(peripheral),
                    ex => _logger.LogWarning(
                        ex,
                        "Pawprint disconnect observer failed"));
            
            // Verify that the Pawprint service is available.
            var service = await peripheral.GetServiceAsync(ServiceUuid, cancelToken: cancellationToken);

            if (service is null)
                throw new InvalidOperationException(
                    $"Pawprint GATT service {ServiceUuid} not found.");
            
            _logger.LogInformation("notificationSubscription");
            _notificationSubscription = peripheral
                .NotifyCharacteristic(
                    ServiceUuid,
                    NotifyCharacteristicUuid)
                .Subscribe(
                    result => Notifications(result.Data),
                    ex =>
                    {
                        _logger.LogError(
                            ex,
                            "Pawprint notification stream failed");

                        OnDisconnected(peripheral);
                    });
            
            _logger.LogInformation("UpdatePawprintSettings");
            
            while (!_receivedStartupMessage &&
                   _state.Value != WebsocketConnectionState.Disconnected)
            {
                await Task.Delay(500);
            }
            
            UpdatePawprintSettings();
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

    private void OnDisconnected(IPeripheral peripheral)
    {
        if (!ReferenceEquals(_peripheral, peripheral))
            return;

        _logger.LogInformation(
            "Pawprint disconnected: {DeviceName}",
            peripheral.Name);

        _notificationSubscription?.Dispose();
        _notificationSubscription = null;

        CurrentlyConnectedDeviceId = "";
        _state.Value = WebsocketConnectionState.Disconnected;
        _battery.Value = 0;
        _receivedStartupMessage = false;
    }

    public async Task Disconnect()
    {
        _logger.LogInformation("Got disconnect");
        if (_peripheral != null) await _peripheral.DisconnectAsync(timeout: new TimeSpan(0, 0, 5));
    }
    
    public void UpdatePawprintSettings()
        => _configTimer?.Change(TimeSpan.FromMilliseconds(1000), Timeout.InfiniteTimeSpan);
    
    private async Task UpdateConfig()
    {
        await _configLock.WaitAsync();
        try
        {
            var pawprintConfig = _config.Config.PawprintsConfig.Pawprints
                .First(x => x.Color == Color);

            if (!_receivedStartupMessage ||
                _state.Value != WebsocketConnectionState.Connected)
            {
                _logger.LogWarning(
                    "Skipping Pawprint configuration because the connection wasn't established"
                );
                return;
            }

            await SendCommand(TriggerMessage.Build(pawprintConfig));
            await ConfigUpdated.Raise();
        }
        catch (InvalidOperationException) { }
        finally
        {
            _configLock.Release();
        }
    }
    
    private async Task SendCommand(byte[] command)
    {
        //_logger.LogInformation("Send: {Bytes}", string.Join(";", command.Select(b => b.ToString("X2"))));
        
        var peripheral = _peripheral
            ?? throw new InvalidOperationException("Pawprint is not connected.");

        var cancellationToken = _connectionCts?.Token
            ?? throw new InvalidOperationException("No active connection.");

        await peripheral
            .WriteCharacteristic(
                ServiceUuid,
                WriteCharacteristicUuid,
                command,
                withResponse: false)
            .ToTask(cancellationToken);
    }

    private void Notifications(byte[]? message)
    {
        _state.Value = WebsocketConnectionState.Connected;
        if (message is not { Length: > 0 })
            return;
        
        //_logger.LogInformation("received: {Bytes}", string.Join(";", message.Select(b => b.ToString("X2"))));
        
        switch (message[0])
        {
            case 0x53:
                _receivedStartupMessage = true;
                break;
            case 0x51:
                var info = new GeneralInfoMessage(message);
                _battery.Value = info.BatteryLevel;
                break;

            case 0x5A:
                var eventActivate = new TriggerActiveMessage(message);
                OnTriggerActivated?.Raise(eventActivate);
                break;

            case 0x5B:
                var eventDeactivate = new TriggerDeactivatedMessage(message);
                OnTriggerDeactivated?.Raise(eventDeactivate);
                break;

            case 0x5C:
                var parameterChange = new ParameterChangeMessage(message);
                OnParameterChanged?.Raise(parameterChange);
                break;

            case 0xD0:
                _currentRawData.Value = new RawInfoMessage(message);
                break;

            case 0xF1:
                var automaticAngles = new ReportAutomaticAngleDetection(message, Color);
                OnAutomaticAngleDetection?.Raise(automaticAngles);
                break;
        }
    }

    public async Task StartAutomaticAngleDetection()
    {
        await SendCommand([0x60]);
    }

    private async Task Close()
    {
        await _connectionLock.WaitAsync();

        try
        {
            _logger.LogInformation("Closing Pawprint connection");

            await DisconnectCoreAsync();
            await _onClose.InvokeAsyncParallel();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task DisconnectCoreAsync()
    {
        _logger.LogInformation("Disconnecting Pawprint connection");
        
        _state.Value = WebsocketConnectionState.Disconnected;
        
        if (_connectionCts != null)
        {
            await _connectionCts.CancelAsync();
            _connectionCts.Dispose();
            _connectionCts = null;
        }

        _notificationSubscription?.Dispose();
        _notificationSubscription = null;

        _disconnectSubscription?.Dispose();
        _disconnectSubscription = null;

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

        CurrentlyConnectedDeviceId = "";
        _state.Value = WebsocketConnectionState.Disconnected;
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing Pawprint connection");
        if (_disposed)
            return;

        _disposed = true;

        await _disposeCts.CancelAsync();
        await Close();

        _disposeCts.Dispose();
        _connectionLock.Dispose();
    }
}
