using MediaBrowser.Model.Plugins;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp;

public class PluginConfiguration : BasePluginConfiguration
{
	public List<string> AdditionalPlaylistUris { get; set; } = [];

	public bool IgnoreServerPlaylist { get; set; }

	public bool ReplacePlaylistHostWithTunerHost { get; set; } = true;

	public int EpgScanDurationSeconds { get; set; } = 30;
}