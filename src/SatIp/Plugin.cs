using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private const string ID = "3ebdc5ac-57aa-4ed0-aedf-122f99a6976f";
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }
    public override string Name => "SAT>IP";

    public override string Description => "A SAT>IP server plugin for Jellyfin.";

    public override Guid Id => new(ID);

    public IEnumerable<PluginPageInfo> GetPages() => [];
}