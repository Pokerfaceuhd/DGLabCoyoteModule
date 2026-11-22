using LucHeart.WebsocketLibrary;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Api;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.SDK.CSharp.Updatables;
using OpenShock.Serialization.Deprecated.DoNotUse.V1;
using OpenShock.Serialization.Types;
using openshock2coyote.Config;
using openshock2coyote.Models.Coyote;
using openshock2coyote.Utils;

namespace openshock2coyote.Services;

public class FlowManager(
    IModuleConfig<Openshock2CoyoteConfig> _config,
    ILogger<FlowManager> _logger,
    ILogger<DeviceConnection> _deviceConnectionLogger,
    ILogger<CoyoteConnection> coyoteConnectionLogger,
    ILogger<PawprintService> pawprintServiceLogger,
    ILogger<PawprintConnection> pawprintConnectionLogger,
    BluetoothScanner bluetoothScanner,
    IOpenShockService _openShockService) : IAsyncDisposable
{
    public Guid HubId { get; private set; } = Guid.Empty;
    private readonly CancellationTokenSource _shutdown = new();
    private static readonly TimeSpan MaxHubRetryDelay = TimeSpan.FromSeconds(60);
    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _simulatedHubState =
        new(WebsocketConnectionState.Disconnected);
    public IAsyncUpdatable<WebsocketConnectionState> SimulatedHubState => _simulatedHubState;
    
    public PawprintService? PawprintService { get; private set; }
    private DeviceConnection? DeviceConnection { get; set; }
    public CoyoteConnection CoyoteConnection { get; private set; } = new(coyoteConnectionLogger, _config);

    public bool CoyoteTemporaryConnect;
    
    private bool _disposed;

    public async Task LoadConfigAndStart()
    {
        CoyoteConnection = new CoyoteConnection(coyoteConnectionLogger, _config);
        
        if (_config.Config.Hub.Hub != null)
        {
            var hubId = _config.Config.Hub.Hub.Value;
            _ = OsTask.Run(() => StartHubConnectionWithRetry(hubId));
        }
        
        PawprintService = new PawprintService(_config, pawprintServiceLogger, pawprintConnectionLogger, bluetoothScanner, _openShockService);
        
        _ = OsTask.Run(AutoConnect);
    }
    
    private async Task StartHubConnectionWithRetry(Guid hubId)
    {
        for (var attempt = 0; !_shutdown.IsCancellationRequested; attempt++)
        {
            try
            {
                if (await SelectedDeviceChanged(hubId)) return;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while starting hub connection");
            }

            var delay = TimeSpan.FromSeconds(Math.Min(5 * (attempt + 1), MaxHubRetryDelay.TotalSeconds));
            _logger.LogWarning("Could not start hub connection, retrying in {Delay:0}s", delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
    
    public async Task<bool> SelectedDeviceChanged(Guid id)
    {
        _config.Config.Hub.Hub = id;
        await _config.Save();

        HubId = id;

        if (HubId == Guid.Empty)
        {
            _logger.LogInformation("Id is empty, stopping connection");
            await StopHubConnection();
            return true;
        }

        _logger.LogInformation("Selected device changed to {Id}", id);
        var deviceDetails = await _openShockService.Api.GetHub(id);

        if (deviceDetails.IsT0)
        {
            var token = deviceDetails.AsT0.Value.Token;
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Token is null or empty, make sure your api token has device.auth permission");
                return false;
            }

            _logger.LogDebug("Starting device connection");

            await StartHubConnection(id, token);
            return true;
        }

        if (deviceDetails.IsT1) _logger.LogError("Hub not found");
        else _logger.LogError("Unauthorized, make sure your logged in");

        await StopHubConnection();
        return false;
    }

    private IAsyncDisposable? _deviceStateSubscription = null;

    private async Task<bool> StopHubConnection()
    {
        if (_deviceStateSubscription != null)
        {
            await _deviceStateSubscription.DisposeAsync();
            _deviceStateSubscription = null;
        }

        if (DeviceConnection == null)
        {
            _simulatedHubState.Value = WebsocketConnectionState.Disconnected;
            return false;
        }

        await DeviceConnection.DisposeAsync();
        DeviceConnection = null;
        _simulatedHubState.Value = WebsocketConnectionState.Disconnected;
        return true;
    }

    private async Task StartHubConnection(Guid id, string authToken)
    {
        await StopHubConnection();

        var deviceConnection =
            new DeviceConnection(_openShockService.Auth.BackendBaseUri, authToken, _deviceConnectionLogger);
        deviceConnection.OnControlMessage += OnControlMessage;
        _deviceStateSubscription = await deviceConnection.State.Updated.SubscribeAsync(state =>
        {
            _simulatedHubState.Value = state;
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        DeviceConnection = deviceConnection;

        await deviceConnection.InitializeAsync().ConfigureAwait(false);
    }

    private async Task OnControlMessage(ShockerCommandList commandList)
    {
        var hubConfig = _config.Config.Hub;

        var shockerStops = commandList.Commands.Where(command => command.Type == ShockerCommandType.Stop);
        foreach (var shockerCommand in shockerStops)
            CoyoteConnection.StopCommand(shockerCommand.Id == _config.Config.Hub.ChannelAId ? Channel.A : Channel.B);

        var packetTasks = commandList.Commands
            .Where(command => (command.Type == ShockerCommandType.Shock || (command.Type == ShockerCommandType.Vibrate && _config.Config.CoyoteConfig.Vibrate))
                              && (command.Id == hubConfig.ChannelAId || command.Id == hubConfig.ChannelBId))
            .Select(command =>
            {
                var channel = command.Id == _config.Config.Hub.ChannelAId ? Channel.A : Channel.B;
                var strength = CalculateStrength(command.Type, command.Intensity);

                return CoyoteConnection.Control(new SingleChannelWaveformSeries(channel, strength, command.Duration, 100));
            }
        );
        await Task.WhenAll(packetTasks);
    }
    
    public byte CalculateStrength(ShockerCommandType type, byte intensity)
    {
        var range = type == ShockerCommandType.Vibrate
            ? _config.Config.CoyoteConfig.VibrateMultiplierRange
            : _config.Config.CoyoteConfig.ShockMultiplierRange;

        var strength = range.Min * 100 + (range.Max - range.Min) * intensity;
        return (byte)Math.Clamp(strength, 1, 200);
    }

    public async Task DisconnectCoyote()
    {
        await CoyoteConnection.Disconnect();
    }
    
    private async Task AutoConnect()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync())
        {
            await Task.Delay(1000);
            await PawprintService?.PawprintAutoConnect()!;
            await CoyoteAutoConnect();
        }
    }
    
    private async Task CoyoteAutoConnect()
    {
        if (CoyoteConnection.State.Value != WebsocketConnectionState.Disconnected || (!_config.Config.CoyoteConfig.AutoConnect && !CoyoteTemporaryConnect))
        {
            CoyoteTemporaryConnect = false;
            return;
        }
        
        try
        {
            var foundCoyote = bluetoothScanner.Devices.First(device => device.Identifier is "47L121000");
            try
            {
                await CoyoteConnection.OpenAsync(foundCoyote);
                CoyoteTemporaryConnect = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to connect to Coyote {DeviceId}",
                    foundCoyote.Address);
            }
        }
        catch (InvalidOperationException) { }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _shutdown.CancelAsync();

        await StopHubConnection();
        
        _shutdown.Dispose();
    }
}
