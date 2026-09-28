using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.LiveTv;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Extensions;

public static class IConfigurationManagerExtensions
{
    /// <summary>
    /// Gets the configuration of the specified type from the configuration manager.
    /// </summary>
    /// <remarks>
    /// This method retrieves the configuration of the specified type from the configuration manager. It 
    /// returns null if the configuration is not found.
    /// I would rather inject IOptions<T> but the options calles are not registered in the DI container, 
    /// so this is a workaround. It seems that inernally jellyfin does the same thing, so this should be 
    /// safe to use.
    /// </remarks>
    /// <typeparam name="T">The type of the configuration to retrieve.</typeparam>
    /// <param name="configurationManager">The configuration manager.</param>
    /// <returns>The configuration instance or null if not found.</returns>
    public static T? GetConfiguration<T>(this IConfigurationManager configurationManager)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(configurationManager);

        var store = configurationManager.GetConfigurationStores()
            .FirstOrDefault(store => store.ConfigurationType == typeof(T));

        if (store == null)
            return null;

        return configurationManager.GetConfiguration(store.Key) as T;
    }

    /// <summary>
    /// Gets the Live TV options from the configuration manager.
    /// </summary>
    /// <param name="configurationManager">The configuration manager.</param>
    /// <returns>The Live TV options or null if not found.</returns>
    public static LiveTvOptions? GetLiveTvOptions(this IConfigurationManager configurationManager)
        => configurationManager.GetConfiguration<LiveTvOptions>();
}
