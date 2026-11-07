using LucHeart.WebsocketLibrary;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Api;
using OpenShock.Desktop.ModuleBase.Config;
using openshock2coyote.Config;
using openshock2coyote.Config.Actions;
using openshock2coyote.Config.Triggers;
using openshock2coyote.Utils;
using openshock2coyote.Utils.PawprintMessages.ReadMessages;

namespace openshock2coyote.Services;

public class PawprintService
{
    private readonly List<ActionState> _activatedActions = [];
    private readonly IModuleConfig<Openshock2CoyoteConfig> _config;
    private readonly ILogger<PawprintService> _logger;
    private readonly ILogger<PawprintConnection> _pawprintConnectionLogger;
    private readonly BluetoothScanner _bluetoothScanner;
    private readonly IOpenShockService _openShockService;
    
    public Dictionary<byte, PawprintConnection> PawprintConnections { get; }


    public PawprintService(
        IModuleConfig<Openshock2CoyoteConfig> config,
        ILogger<PawprintService> logger,
        ILogger<PawprintConnection> pawprintConnectionLogger,
        BluetoothScanner bluetoothScanner,
        IOpenShockService openShockService)
    {
        _config = config;
        _logger = logger;
        _pawprintConnectionLogger = pawprintConnectionLogger;
        _bluetoothScanner = bluetoothScanner;
        _openShockService = openShockService;
        PawprintConnections = new Dictionary<byte, PawprintConnection>();
        CreatePawprintConnections();
        OsTask.Run(AutoConnect);
        OsTask.Run(ActionLoop);
    }
    
    private void CreatePawprintConnections()
    {
        for (byte i = 1; i < 7; i++)
        {
            PawprintConnections.Add(i, new PawprintConnection(_pawprintConnectionLogger, _config, i));
            PawprintConnections[i].OnParameterChanged += OnPawprintParameterChange;
            PawprintConnections[i].OnTriggerDeactivated += OnPawprintDeactivation;
            PawprintConnections[i].OnTriggerActivated += OnPawprintActivation;
            PawprintConnections[i].OnAutomaticAngleDetection += OnPawprintAutomaticAngleDetection;

            var i1 = i;
            PawprintConnections[i].State.Updated.SubscribeAsync(state =>
            {
                if (state == WebsocketConnectionState.Disconnected)
                {
                    StopAllActionsFromPawprint(i1);
                }
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }
    
    private async Task OnPawprintActivation(TriggerActiveMessage message, BaseTrigger trigger)
    {
        _logger.LogInformation("Pawprint Activation received Color: {MessageMainIndicatorColor}, EventID: {MessageEventId}, Parameter: {MessageParameterValue}", message.MainIndicatorColor, message.EventId, message.ParameterValue);
        
        var triggerActions = GetActionsFromMessage(message.EventId, trigger);
        var actions = _config.Config.PawprintsConfig.Actions
            .Where(action => triggerActions.Contains(action.Guid));
        
        
        
        foreach (var action in actions)
        {
            _activatedActions.Add(new ActionState
            {
                ParameterValue = message.ParameterValue,
                TriggeredByColor = message.MainIndicatorColor,
                TriggerEventId = message.EventId,
                ActionGuid = action.Guid,
                ParameterIncreaseSet = GetParameterIncreaseSet(message.EventId, trigger)
            });
        }
    }
    
    private async Task OnPawprintDeactivation(TriggerDeactivatedMessage message, BaseTrigger trigger)
    {
        _logger.LogInformation("Pawprint Deactivation received Color: {MessageMainIndicatorColor}, EventID: {MessageEventId}", message.MainIndicatorColor, message.EventId);

        var triggerActions = GetActionsFromMessage(message.EventId, trigger);
        var actions = _config.Config.PawprintsConfig.Actions
            .Where(action => triggerActions.Contains(action.Guid));
        
        _activatedActions.RemoveAll(state => 
            actions.Select(a => a.Guid).Contains(state.ActionGuid) 
            && state.TriggeredByColor == message.MainIndicatorColor 
            && state.TriggerEventId == message.EventId);
    }
    
    private async Task OnPawprintParameterChange(ParameterChangeMessage message, BaseTrigger trigger)
    {
        _logger.LogInformation("Pawprint Parameter Changed received Color: {MessageMainIndicatorColor}, EventID: {MessageEventId}, Parameter: {MessageParameterValue}", message.MainIndicatorColor, message.EventId, message.ParamaterValue);
        
        var triggerActions = GetActionsFromMessage(message.EventId, trigger);
        var actions = _config.Config.PawprintsConfig.Actions
            .Where(action => triggerActions.Contains(action.Guid));

        foreach (var action in _activatedActions.Where(state =>
                     actions.Select(a => a.Guid).Contains(state.ActionGuid)
                     && state.TriggeredByColor == message.MainIndicatorColor
                     && state.TriggerEventId == message.EventId))
        {
            action.ParameterValue = message.ParamaterValue;
        }
        
    }
    
    private async Task OnPawprintAutomaticAngleDetection(ReportAutomaticAngleDetection message)
    {
        var pawprint = _config.Config.PawprintsConfig.Pawprints.First(pawprint => pawprint.color == message.Color);
        switch (pawprint.Trigger)
        {
            case PressReleaseTriggerAndAccelerationAngleTrigger angleMode:
                if (angleMode.AccelerationOrAngleSubmode == AccelerationAngleSubmode.Angle)
                {
                    angleMode.RangeX = message.RangeX;
                    angleMode.RangeY = message.RangeY;
                    angleMode.RangeZ = message.RangeZ;
                }
                else
                {
                    _logger.LogWarning("Automatic Angle Detection was returned for a Pawprint not in Angle Mode");
                }
                break;
            default:
                _logger.LogWarning("Automatic Angle Detection was returned for a Pawprint not in Angle Mode");
                break;
        }
    }

    private List<Guid> GetActionsFromMessage(byte eventId, BaseTrigger trigger)
    {
        return trigger switch
        {
            PressReleaseTriggerAndAccelerationAngleTrigger button => eventId == 1
                ? button.ActionsOnPressRelease
                : button.ActionsOnAngleAcceleration,
            ProbabilityTrigger probability => probability.ProbabilityActions[eventId - 1],
            _ => trigger.Actions
        };
    }
    
    public void StopAllActionsFromPawprint(byte color)
    {
        _activatedActions.RemoveAll(state => state.TriggeredByColor == color);
    }

    private Boolean GetParameterIncreaseSet(byte eventId, BaseTrigger trigger)
    {
        switch (trigger)
        {
            case PressReleaseTriggerAndAccelerationAngleTrigger button:
                if (eventId == 1)
                {
                    return button.PressReleaseParameterIncreaseOnActivation != 0 
                           || button.PressReleaseParameterIncreaseRateWhileActive != 0;
                }
                return button.AccelerationAngleParameterMappingRange != 0;
            case ExternalVoltageDetectionTrigger parameterMapping:
                return parameterMapping.ParameterMappingRange != 0;
            case RandomReactionTrigger reaction:
                return reaction.ParameterIncreaseOnActivation != 0 
                       || reaction.ParameterIncreaseRateWhileActive != 0;
            default:
                return false;
        }
    }

    private async Task ActionLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync())
        {
            var actions = _activatedActions.Select(a => _config.Config.PawprintsConfig.Actions
                .First(action => action.Guid == a.ActionGuid).Name).ToArray();
            if (actions.Count() != 0)
            {
                _logger.LogInformation("Activated Actions: {ActivatedActions}", string.Join(", ", actions));
            }

            foreach (var activatedAction in _activatedActions)
            {
                var storedAction = _config.Config.PawprintsConfig.Actions
                    .First(action => action.Guid == activatedAction.ActionGuid);

                switch (storedAction)
                {
                    case OpenshockAction openshockAction:
                        byte intensity;
                        if (activatedAction.ParameterIncreaseSet)
                        {
                            intensity = (byte)Math.Round(openshockAction.IntensityRange.Min
                                                         + (activatedAction.ParameterValue / 255.0)
                                                         * (openshockAction.IntensityRange.Max -
                                                            openshockAction.IntensityRange.Min));
                        }
                        else
                        {
                            intensity = openshockAction.DefaultIntensity;
                        }

                        _logger.LogInformation("Activated Shocker {Intensity} {Type} {Shockers}", intensity, 
                            openshockAction.Type, string.Join(", ", openshockAction.Shockers));
                        _openShockService.Control.LiveControl(openshockAction.Shockers, intensity, openshockAction.Type);
                        break;
                }
            }
        }
    }

    private async Task AutoConnect()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync())
        {
            if (!_config.Config.PawprintsConfig.AutoConnect)
                continue;

            var connectedPawprints = PawprintConnections
                .Where(pc => pc.Value.State.Value == WebsocketConnectionState.Connected)
                .Select(pc => pc.Value.CurrentlyConnectedDeviceId);

            var enumerable = connectedPawprints as string[] ?? connectedPawprints.ToArray();
            var bluetoothDevices = _bluetoothScanner.Devices.Where(device =>
                device.Name is "47L120300" && !enumerable.Contains(device.Uuid)).ToArray();
            
            foreach (var bluetoothDevice in bluetoothDevices)
            {
                var connection = PawprintConnections
                    .Select(pc => pc.Value)
                    .Where(pc => _config.Config.PawprintsConfig.Pawprints
                        .First(p => pc.Color == p.color).Enabled)
                    .FirstOrDefault(pc => (byte)pc!.State.Value < 2 || (byte)pc.State.Value == 4, defaultValue: null);

                if (connection is null)
                {
                    _logger.LogInformation("Found no available connections");
                    break;
                }

                try
                {
                    await connection.OpenAsync(bluetoothDevice);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to connect to Pawprint {DeviceId}",
                        bluetoothDevice.Uuid);
                    _bluetoothScanner.RemoveDevice(bluetoothDevice.Uuid);
                }
            }
        }
    }
}

public class ActionState
{
    public required byte ParameterValue { get; set; }
    public required byte TriggeredByColor;
    public required byte TriggerEventId;
    public required Guid ActionGuid;
    public required bool ParameterIncreaseSet;
}