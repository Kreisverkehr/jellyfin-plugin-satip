using Kreisverkehr.Jellyfin.Plugin.SatIp.Dvb;
using Kreisverkehr.Jellyfin.Plugin.SatIp.Rtsp;
using MediaBrowser.Controller;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddUpnp();

        serviceCollection.AddSingleton<ISatIpTunerHost, TunerHost>();
        serviceCollection.AddSingleton<ITunerHost>(sp => sp.GetRequiredService<ISatIpTunerHost>());
        serviceCollection.AddSingleton<ISatIpListingsProvider, ListingsProvider>();
        serviceCollection.AddSingleton<IListingsProvider>(sp => sp.GetRequiredService<ISatIpListingsProvider>());
        serviceCollection.AddSingleton<IProgramInfoFactory, ProgramInfoFactory>();
        serviceCollection.AddSingleton<IRtspSessionFactory, RtspSessionFactory>();
        serviceCollection.AddSingleton<ISatIpLiveStreamFactory, SatIpLiveStreamFactory>();
        serviceCollection.AddKeyedSingleton<IEpgScanner, Dvb.EpgScanner>("dvb");
    }
}