using OpenShock.Desktop.ModuleBase;
using DGLabCoyote;
using DGLabCoyote.Config;
using DGLabCoyote.Services;
using OpenShock.Desktop.ModuleBase.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OpenShock.Desktop.ModuleBase.Config;
using DGLabCoyote.Ui.Pages.Dash.Tabs;

[assembly:DesktopModule(typeof(DGLabCoyoteModule), "DGLabCoyote", "DGLabCoyote")]

namespace DGLabCoyote;

public class DGLabCoyoteModule : DesktopModuleBase
{
    public override string IconPath => "DGLabCoyote/Resources/DGLabCoyote-Icon.png";
    
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
        var config = await ModuleInstanceManager.GetModuleConfig<DgLabCoyoteConfig>();
        ModuleServiceProvider = BuildServices(config);
        
    }
    
    private ServiceProvider BuildServices(IModuleConfig<DgLabCoyoteConfig> config)
    {
        var loggerFactory = ModuleInstanceManager.AppServiceProvider.GetRequiredService<ILoggerFactory>();
        
        var services = new ServiceCollection();

        services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddSingleton(config);

        services.AddSingleton(ModuleInstanceManager.OpenShock);
        
        services.AddSingleton<FlowManager>();
        services.AddSingleton<BluetoothService>();
        
        return services.BuildServiceProvider();
    }   

    public override async Task Start()
    {
        await ModuleServiceProvider.GetRequiredService<FlowManager>().LoadConfigAndStart();
    }
}