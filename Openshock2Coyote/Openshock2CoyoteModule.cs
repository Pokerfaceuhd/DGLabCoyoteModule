using OpenShock.Desktop.ModuleBase;
using openshock2coyote;
using OpenShock.Desktop.ModuleBase.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.Desktop.ModuleBase.Models;
using openshock2coyote.Ui.Pages.Dash.Tabs;
using openshock2coyote.Config;
using openshock2coyote.Services;

[assembly:RequiredPermission(TokenPermissions.Devices_Auth)]
[assembly:RequiredPermission(TokenPermissions.Devices_Edit)]
[assembly:DesktopModule(typeof(Openshock2CoyoteModule), "openshock2coyote", "openshock2coyote")]

namespace openshock2coyote;

public class Openshock2CoyoteModule : DesktopModuleBase
{
    public override string IconPath => "openshock2coyote/Resources/openshock2coyote-Icon.png";
    
    public override IReadOnlyCollection<NavigationItem> NavigationComponents { get; } =
    [
        new()
        {
            Name = "Settings",
            ComponentType = typeof(Settings),
            Icon = IconOneOf.FromSvg(Icons.Material.Filled.Settings)
        }
    ];
    
    public override async Task Setup()
    {
        var config = await ModuleInstanceManager.GetModuleConfig<Openshock2CoyoteConfig>();
        ModuleServiceProvider = BuildServices(config);
        
    }
    
    private ServiceProvider BuildServices(IModuleConfig<Openshock2CoyoteConfig> config)
    {
        var loggerFactory = ModuleInstanceManager.AppServiceProvider.GetRequiredService<ILoggerFactory>();
        
        var services = new ServiceCollection();

        services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddSingleton(config);

        services.AddSingleton(ModuleInstanceManager.OpenShock);
        
        services.AddSingleton<FlowManager>();
        
        return services.BuildServiceProvider();
    }   

    public override async Task Start()
    {
        await ModuleServiceProvider.GetRequiredService<FlowManager>().LoadConfigAndStart();
    }
}