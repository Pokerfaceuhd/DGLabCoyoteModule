using System.Windows;
using InTheHand.Bluetooth;
using LucHeart.WebsocketLibrary;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Api;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.SDK.CSharp.Updatables;
using OpenShock.Serialization.Gateway;
using OpenShock.Serialization.Types;
using openshock2coyote.Config;
using openshock2coyote.Models.Coyote;

namespace openshock2coyote.Services;

public class FlowManager
{
    public Guid HubId { get; private set; } = Guid.Empty;
    
    public DeviceConnection? DeviceConnection { get; private set; } = null;
    public CoyoteConnection? CoyoteConnection { get; private set; } = null;
    
    private readonly IModuleConfig<Openshock2CoyoteConfig> _config;
    private readonly ILogger<FlowManager> _logger;
    private readonly ILogger<DeviceConnection> _deviceConnectionLogger;
    private readonly ILogger<CoyoteConnection> _coyoteConnectionLogger;
    private readonly IOpenShockService _openShockService;
    
    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _deviceConnectionState =
        new(WebsocketConnectionState.Disconnected);
    public IAsyncUpdatable<WebsocketConnectionState> DeviceConnectionState => _deviceConnectionState;
    
    private readonly AsyncUpdatableVariable<WebsocketConnectionState> _coyoteConnectionState =
        new(WebsocketConnectionState.Disconnected);
    
    public IAsyncUpdatable<WebsocketConnectionState> CoyoteConnectionState => _coyoteConnectionState;
    
    private readonly AsyncUpdatableVariable<byte> _batteryLevel = new(0);
    public IAsyncUpdatable<byte> BatteryLevel => _batteryLevel;
    
    public FlowManager(
        IModuleConfig<Openshock2CoyoteConfig> config,
        ILogger<FlowManager> logger,
        ILogger<DeviceConnection> deviceConnectionLogger,
        ILogger<CoyoteConnection> coyoteConnectionLogger,
        IOpenShockService openShockService)
    {
        _config = config;
        _logger = logger;
        _deviceConnectionLogger = deviceConnectionLogger;
        _coyoteConnectionLogger = coyoteConnectionLogger;
        _openShockService = openShockService;
    }
    
    public async Task LoadConfigAndStart()
    {
        if (_config.Config.BluetoothConnection.CoyoteAddress != string.Empty)
            await ConnectCoyote();
        
        if (_config.Config.Hub.Hub != Guid.Empty)
            await SelectedDeviceChanged(_config.Config.Hub.Hub);
    }
    
    public async Task SelectedDeviceChanged(Guid id)
    {
        _config.Config.Hub.Hub = id;
        await _config.Save();
        
        HubId = id;
        
        if (HubId == Guid.Empty)
        {
            _logger.LogError("Id is empty, stopping connection");
            await StopHubConnection();
            return;
        }
        
        _logger.LogInformation("Selected device changed to {Id}", id);
        var deviceDetails = await _openShockService.Api.GetHub(id);

        
        if (deviceDetails.IsT0)
        {
            var token = deviceDetails.AsT0.Value.Token;
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Token is null or empty, make sure your api token has device.auth permission");
                return;
            }
            
            _logger.LogDebug("Starting device connection");

            await StartHubConnection(id, token);
            return;
        }

       
        deviceDetails.Switch(success => {}, found =>
            {
                _logger.LogError("Hub not found");
            },
            error =>
            {
                _logger.LogError("Unauthorized, make sure your logged in");
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
            new DeviceConnection(_openShockService.Auth.BackendBaseUri, authToken, _deviceConnectionLogger);
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
        
        var hubConfig = _config.Config.Hub;
        
        var packetTasks = commandList.Commands
            .SkipWhile(command => command.Type != ShockerCommandType.Shock || command.Id != hubConfig.ChannelAId && command.Id != hubConfig.ChannelBId)
            .Select(command =>
            {
                Channel channel;
                channel = command.Id == _config.Config.Hub.ChannelAId ? Channel.A : Channel.B;
                
                return CoyoteConnection.Control(new SingleChannelWaveformSeries(channel, command.Duration, command.Intensity));
            }
        );
        await Task.WhenAll(packetTasks);
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
        var coyoteAddress = _config.Config.BluetoothConnection.CoyoteAddress;
        if (coyoteAddress == string.Empty)
        {
            return;
        }
        if (CoyoteConnection != null)
        {
            await CoyoteConnection.DisposeAsync();
            CoyoteConnection = null;
        }

        CoyoteConnection = new CoyoteConnection(_coyoteConnectionLogger, _config, coyoteAddress);
        
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
        CoyoteConnection.OpenAsync().ConfigureAwait(false);
    }
}