using LucHeart.WebsocketLibrary;
using LucHeart.WebsocketLibrary.Updatables;
using LucHeart.WebsocketLibrary.Utils;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.MinimalEvents;
using openshock2coyote.Config;
using openshock2coyote.Utils;
using openshock2coyote.Utils.PawprintMessages.ReadMessages;
using openshock2coyote.Utils.PawprintMessages.WriteMessages;
using SimpleSharpBLE;

namespace openshock2coyote;

public class PawprintConnection : IAsyncDisposable
{
    private readonly ILogger<PawprintConnection> _logger;
    private readonly IModuleConfig<Openshock2CoyoteConfig> _config;
    public readonly byte Color;

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private CancellationTokenSource? _connectionCts;
    private Peripheral? _peripheral;

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

    public async Task OpenAsync(Peripheral peripheral)
    {
        ArgumentNullException.ThrowIfNull(peripheral);

        await _connectionLock.WaitAsync(_disposeCts.Token);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Already connecting or connected to this device.
            if (_peripheral?.Address == peripheral.Address &&
                _state.Value is WebsocketConnectionState.Connecting or WebsocketConnectionState.Connected)
            {
                _logger.LogDebug(
                    "Ignoring duplicate connection attempt for {Uuid}",
                    peripheral.Address);

                return;
            }

            if (_peripheral != null)
                await DisconnectCoreAsync();

            _connectionCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _disposeCts.Token);

            var cancellationToken = _connectionCts.Token;

            _peripheral = peripheral;
            CurrentlyConnectedDeviceId = peripheral.Address;
            _state.Value = WebsocketConnectionState.Connecting;
            
            _logger.LogInformation(
                "Connecting to Pawprint: {DeviceName} ({DeviceId}) Strength {Rssi}dBm",
                peripheral.Identifier,
                peripheral.Address,
                peripheral.Rssi);
            
            await peripheral.ConnectAsync(cancellationToken);

            _logger.LogInformation(
                "Connected to Pawprint: {DeviceUuid}",
                peripheral.Address);
            
            peripheral.Disconnected += (_, _) => OnDisconnected(peripheral);
            
            // Verify that the Pawprint service is available.
            var service = peripheral.Services.FirstOrDefault(s => s?.Uuid == ServiceUuid, null);

            if (service is null)
                throw new InvalidOperationException(
                    $"Pawprint GATT service {ServiceUuid} not found.");
            
            _logger.LogInformation("notificationSubscription");
            await peripheral.NotifyAsync(ServiceUuid, NotifyCharacteristicUuid, Notifications, cancellationToken);
            
            while (!_receivedStartupMessage &&
                   _state.Value != WebsocketConnectionState.Disconnected)
            {
                await Task.Delay(500, cancellationToken);
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

    private void OnDisconnected(Peripheral peripheral)
    {
        if (!ReferenceEquals(_peripheral, peripheral))
            return;

        _logger.LogInformation(
            "Pawprint disconnected: {DeviceName}",
            peripheral.Identifier);

        peripheral.UnsubscribeAsync(ServiceUuid, NotifyCharacteristicUuid);
        
        _logger.LogInformation("Unsubscribed");
        
        CurrentlyConnectedDeviceId = "";
        _state.Value = WebsocketConnectionState.Disconnected;
        _battery.Value = 0;
        _receivedStartupMessage = false;
    }

    public async Task Disconnect()
    {
        _logger.LogInformation("Disconnect requested");

        // Interrupt an ongoing ConnectAsync or GATT operation immediately.
        var activeCts = Volatile.Read(ref _connectionCts);

        try
        {
            activeCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The connection was already cleaned up.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel active connection");
        }

        await _connectionLock.WaitAsync();

        try
        {
            await DisconnectCoreAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
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

        await peripheral.WriteCommandAsync(ServiceUuid, WriteCharacteristicUuid, command, cancellationToken);
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

        // Capture current references before clearing them.
        var peripheral = _peripheral;
        var connectionCts = _connectionCts;

        // Prevent further operations from using these references.
        _peripheral = null;
        _connectionCts = null;

        // Reset connection state.
        _state.Value = WebsocketConnectionState.Disconnected;
        CurrentlyConnectedDeviceId = "";
        _battery.Value = 0;
        _receivedStartupMessage = false;

        // Stop pending configuration updates.
        _configTimer?.Change(
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        // Dispose subscriptions.
        peripheral?.UnsubscribeAsync(ServiceUuid, NotifyCharacteristicUuid);

        // Cancel and dispose the connection token.
        if (connectionCts != null)
        {
            try
            {
                await connectionCts.CancelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cancelling connection token");
            }
            finally
            {
                connectionCts.Dispose();
            }
        }

        if (peripheral == null)
            return;
        
        await peripheral.DisconnectAsync();
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
