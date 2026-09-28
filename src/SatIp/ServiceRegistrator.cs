using MediaBrowser.Controller;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddUpnp(o =>
        {
            o.DescriptionType = typeof(Upnp.SatIpDescription);
        });
        serviceCollection.AddSingleton<ITunerHost, TunerHost>();
    }
}