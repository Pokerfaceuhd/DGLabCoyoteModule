
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
using openshock2coyote.Config.Triggers;
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
    public byte Battery { get; private set; }

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
    
    public event Func<TriggerActiveMessage, BaseTrigger, Task>? OnTriggerActivated;
    public event Func<TriggerDeactivatedMessage, BaseTrigger, Task>? OnTriggerDeactivated;
    public event Func<ParameterChangeMessage, BaseTrigger, Task>? OnParameterChanged;
    public event Func<ReportAutomaticAngleDetection, Task>? OnAutomaticAngleDetection;

    public PawprintConnection(
        ILogger<PawprintConnection> logger,
        IModuleConfig<Openshock2CoyoteConfig> config,
        byte color)
    {
        _logger = logger;
        _config = config;
        Color = color;
    }

    public async Task OpenAsync(IPeripheral peripheral)
    {
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
            CurrentlyConnectedDeviceId = peripheral.Uuid;
            _state.Value = WebsocketConnectionState.Connecting;

            _logger.LogInformation(
                "Connecting to Pawprint: {DeviceName} ({DeviceId})",
                peripheral.Name,
                peripheral.Uuid);

            // Subscribe before connecting so we cannot miss the connection.
            var connectedTask = peripheral
                .WhenConnected()
                .Take(1)
                .Timeout(TimeSpan.FromSeconds(12))
                .ToTask(cancellationToken);

            // PawprintService handles reconnection itself.
            peripheral.Connect(new ConnectionConfig(AutoConnect: false));

            await connectedTask;

            _logger.LogInformation(
                "Connected to Pawprint: {DeviceName}",
                peripheral.Name);

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

            _notificationSubscription = peripheral
                .NotifyCharacteristic(
                    ServiceUuid,
                    NotifyCharacteristicUuid)
                .Subscribe(
                    result => Notifications(result.Data.ToArray()),
                    ex =>
                    {
                        _logger.LogError(
                            ex,
                            "Pawprint notification stream failed");

                        OnDisconnected(peripheral);
                    });
            
            await UpdateConfig();
            
            _logger.LogInformation(
                "Pawprint GATT notifications enabled: {DeviceName}",
                peripheral.Name);
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
    }

    public async Task UpdateConfig()
    {
        var pawprintConfig = _config.Config.PawprintsConfig.Pawprints
            .First(x => x.color == Color);
        
        await SendCommand(TriggerMessage.Build(pawprintConfig));
    }
    
    private async Task SendCommand(byte[] command)
    {
        _logger.LogInformation("Send: {Bytes}", string.Join(";", command.Select(b => b.ToString("X2"))));
        
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
        
        _logger.LogInformation("received: {Bytes}", string.Join(";", message.Select(b => b.ToString("X2"))));
        
        switch (message[0])
        {
            case 0x51:
                var info = new GeneralInfoMessage(message);
                Battery = info.BatteryLevel;
                break;

            case 0x5A:
                var eventActivate = new TriggerActiveMessage(message);
                OnTriggerActivated?.Raise(eventActivate, _config.Config.PawprintsConfig.Pawprints[Color-1].Trigger);
                break;

            case 0x5B:
                var eventDeactivate = new TriggerDeactivatedMessage(message);
                OnTriggerDeactivated?.Raise(eventDeactivate, _config.Config.PawprintsConfig.Pawprints[Color-1].Trigger);
                break;

            case 0x5C:
                var parameterChange = new ParameterChangeMessage(message);
                OnParameterChanged?.Raise(parameterChange, _config.Config.PawprintsConfig.Pawprints[Color-1].Trigger);
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

    public async Task Close()
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

    private async Task DisconnectCoreAsync()
    {
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
        if (_disposed)
            return;

        _disposed = true;

        await _disposeCts.CancelAsync();
        await Close();

        _disposeCts.Dispose();
        _connectionLock.Dispose();
    }
}
