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
    IModuleConfig<Openshock2CoyoteConfig> config,
    ILogger<FlowManager> logger,
    ILogger<DeviceConnection> deviceConnectionLogger,
    ILogger<CoyoteConnection> coyoteConnectionLogger,
    ILogger<PawprintService> pawprintServiceLogger,
    ILogger<PawprintConnection> pawprintConnectionLogger,
    BluetoothScanner bluetoothScanner,
    IOpenShockService openShockService)
{
    private const int MaxAutoConnectMult = 60;

    public Guid HubId { get; private set; } = Guid.Empty;

    public PawprintService? PawprintService { get; private set; }
    private DeviceConnection? DeviceConnection { get; set; }
    public CoyoteConnection CoyoteConnection { get; private set; }

    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _deviceConnectionState =
        new(WebsocketConnectionState.Disconnected);
    public IAsyncUpdatable<WebsocketConnectionState> DeviceConnectionState => _deviceConnectionState;

    public bool CoyoteTemporaryConnect = false;

    public async Task LoadConfigAndStart()
    {
        CoyoteConnection = new CoyoteConnection(coyoteConnectionLogger, config);
        
        if (config.Config.Hub.Hub != Guid.Empty)
            await SelectedDeviceChanged(config.Config.Hub.Hub);
        
        PawprintService = new PawprintService(config, pawprintServiceLogger, pawprintConnectionLogger, bluetoothScanner, openShockService);
        
        _ = OsTask.Run(AutoConnect);
    }

    public async Task SelectedDeviceChanged(Guid id)
    {
        config.Config.Hub.Hub = id;
        await config.Save();

        HubId = id;

        if (HubId == Guid.Empty)
        {
            logger.LogError("Id is empty, stopping connection");
            await StopHubConnection();
            return;
        }

        logger.LogInformation("Selected device changed to {Id}", id);
        var deviceDetails = await openShockService.Api.GetHub(id);

        if (deviceDetails.IsT0)
        {
            var token = deviceDetails.AsT0.Value.Token;
            if (string.IsNullOrEmpty(token))
            {
                logger.LogError("Token is null or empty, make sure your api token has device.auth permission");
                return;
            }

            logger.LogDebug("Starting device connection");

            await StartHubConnection(id, token);
            return;
        }


        deviceDetails.Switch( success => { }, found =>
            {
                logger.LogError("Hub not found");
            },
            error =>
            {
                logger.LogError("Unauthorized, make sure your logged in");
            });

        throw new Exception("Unhandled OneOf type");
    }

    private async Task<bool> StopHubConnection()
    {
        if (DeviceConnection == null) return false;
        await DeviceConnection.DisposeAsync();
        DeviceConnection = null;
        _deviceConnectionState.Value = WebsocketConnectionState.Disconnected;
        return true;
    }

    private async Task StartHubConnection(Guid id, string authToken)
    {
        await StopHubConnection();

        DeviceConnection =
            new DeviceConnection(openShockService.Auth.BackendBaseUri, authToken, deviceConnectionLogger);
        DeviceConnection.OnControlMessage += OnControlMessage;
        await DeviceConnection.State.Updated.SubscribeAsync(state =>
        {
            _deviceConnectionState.Value = state;
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        await DeviceConnection.InitializeAsync().ConfigureAwait(false);
    }

    private async Task OnControlMessage(ShockerCommandList commandList)
    {
        if (CoyoteConnection == null) return;

        var hubConfig = config.Config.Hub;

        var shockerStops = commandList.Commands.Where(command => command.Type == ShockerCommandType.Stop);
        foreach (var shockerCommand in shockerStops)
            CoyoteConnection.StopCommand(shockerCommand.Id == config.Config.Hub.ChannelAId ? Channel.A : Channel.B);

        var packetTasks = commandList.Commands
            .Where(command => (command.Type == ShockerCommandType.Shock || (command.Type == ShockerCommandType.Vibrate && config.Config.CoyoteConfig.Vibrate))
                              && (command.Id == hubConfig.ChannelAId || command.Id == hubConfig.ChannelBId))
            .Select(command =>
            {
                var channel = command.Id == config.Config.Hub.ChannelAId ? Channel.A : Channel.B;
                var strength = CalculateStrength(command.Type, command.Intensity);

                return CoyoteConnection.Control(new SingleChannelWaveformSeries(channel, strength, command.Duration, 100));
            }
        );
        await Task.WhenAll(packetTasks);
    }
    
    public byte CalculateStrength(ShockerCommandType type, byte intensity)
    {
        var range = type == ShockerCommandType.Vibrate
            ? config.Config.CoyoteConfig.VibrateMultiplierRange
            : config.Config.CoyoteConfig.ShockMultiplierRange;

        var strength = range.Min * 100 + (range.Max - range.Min) * intensity;
        return (byte)Math.Clamp(strength, 1, 200);
    }

    public async Task DisconnectCoyote()
    {
        if (CoyoteConnection.State.Value == WebsocketConnectionState.Connected)
        {
            await CoyoteConnection.Disconnect();
        }
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
        if (CoyoteConnection.State.Value != WebsocketConnectionState.Disconnected || (!config.Config.CoyoteConfig.AutoConnect && !CoyoteTemporaryConnect))
        {
            return;
        }
        
        try
        {
            var foundCoyote = bluetoothScanner.Devices.First(device => device.Name is "47L121000");
            try
            {
                await CoyoteConnection.OpenAsync(foundCoyote);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to connect to Coyote {DeviceId}",
                    foundCoyote.Uuid);
                
                bluetoothScanner.RemoveDevice(foundCoyote.Uuid);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
    
    
}
