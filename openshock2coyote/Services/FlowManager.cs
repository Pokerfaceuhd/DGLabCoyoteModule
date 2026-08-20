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
    IOpenShockService openShockService)
{
    private const int MaxAutoConnectMult = 60;

    public Guid HubId { get; private set; } = Guid.Empty;

    private DeviceConnection? DeviceConnection { get; set; }
    public CoyoteConnection? CoyoteConnection { get; private set; }

    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _deviceConnectionState =
        new(WebsocketConnectionState.Disconnected);
    public IAsyncUpdatable<WebsocketConnectionState> DeviceConnectionState => _deviceConnectionState;

    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _coyoteConnectionState =
        new(WebsocketConnectionState.Disconnected);

    public IAsyncUpdatable<WebsocketConnectionState> CoyoteConnectionState => _coyoteConnectionState;

    private readonly AsyncUpdatableVariable<byte> _batteryLevel = new(0);
    public IAsyncUpdatable<byte> BatteryLevel => _batteryLevel;

    private CancellationTokenSource _autoConnectCancellationTokenSource = new();

    public async Task LoadConfigAndStart()
    {
        if (config.Config.CoyoteConfig.CoyoteAddress != string.Empty)
            await ConnectCoyote();

        if (config.Config.Hub.Hub != Guid.Empty)
            await SelectedDeviceChanged(config.Config.Hub.Hub);

        StartAutoConnect();
    }

    public void StartAutoConnect()
    {
        if (config.Config.CoyoteConfig.AutoConnect)
        {
            OsTask.Run(AutoConnect);
        }
        else
        {
            _autoConnectCancellationTokenSource.Cancel();
            _autoConnectCancellationTokenSource = new CancellationTokenSource();
        }
    }

    private async Task AutoConnect()
    {
        var intervalMult = 1;
        while (config.Config.CoyoteConfig.AutoConnect)
        {
            await Task.Delay(1000 * intervalMult, _autoConnectCancellationTokenSource.Token);
            while (_coyoteConnectionState.Value != WebsocketConnectionState.Disconnected && config.Config.CoyoteConfig.AutoConnect)
            {
                intervalMult = 1;
                await Task.Delay(3000, _autoConnectCancellationTokenSource.Token);
            }
            await FindCoyote();

            intervalMult++;
            if (intervalMult > MaxAutoConnectMult)
            {
                intervalMult = MaxAutoConnectMult;
            }
        }
    }

    private async Task FindCoyote()
    {
        await ConnectCoyote();
        await Task.Delay(1000, _autoConnectCancellationTokenSource.Token);
        while (CoyoteConnectionState.Value == WebsocketConnectionState.Connecting)
        {
            await Task.Delay(100, _autoConnectCancellationTokenSource.Token);
        }

        if (CoyoteConnectionState.Value == WebsocketConnectionState.Connected)
        {
            return;
        }
        logger.LogInformation("Searching for coyote devices...");
        var bluetoothDevices = await BluetoothService.GetBluetoothDevices();
        try
        {
            var deviceId = bluetoothDevices.First(device => device.Name == "47L121000").Id;
            logger.LogInformation("Coyote V3 found");
            config.Config.CoyoteConfig.CoyoteAddress = deviceId;
            config.SaveDeferred();
            await ConnectCoyote();
        }
        catch (InvalidOperationException)
        {
            logger.LogInformation("Coyote could not be found");
        }
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


        deviceDetails.Switch(success => { }, found =>
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

        var fraction = range.Min + (range.Max - range.Min) * (intensity / 100f);
        return (byte)Math.Clamp(fraction * 200, 1, 200);
    }

    public async Task DisconnectCoyote()
    {
        if (CoyoteConnection != null) await CoyoteConnection.DisposeAsync();
        CoyoteConnection = null;
        _coyoteConnectionState.Value = WebsocketConnectionState.Disconnected;
        _batteryLevel.Value = 0;
    }

    public async Task ConnectCoyote()
    {
        var coyoteAddress = config.Config.CoyoteConfig.CoyoteAddress;
        if (coyoteAddress == string.Empty)
        {
            return;
        }
        if (CoyoteConnection != null)
        {
            await CoyoteConnection.DisposeAsync();
            CoyoteConnection = null;
        }

        CoyoteConnection = new CoyoteConnection(coyoteConnectionLogger, config, coyoteAddress);

        await CoyoteConnection.State.Updated.SubscribeAsync(state =>
        {
            _coyoteConnectionState.Value = state;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await CoyoteConnection.BatteryLevel.Updated.SubscribeAsync(batteryLevel =>
        {
            _batteryLevel.Value = batteryLevel;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        _ = CoyoteConnection.OpenAsync().ConfigureAwait(false);
    }
}
