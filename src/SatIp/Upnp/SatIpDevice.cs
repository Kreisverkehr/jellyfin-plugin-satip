using System.Xml.Serialization;
using Kreisverkehr.NetUpnp.Model;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Upnp;

public class SatIpDevice : UpnpDevice
{
    [XmlElement("X_SATIPCAP", Namespace = "urn:ses-com:satip")]
    public string? SatIpCapabilities { get; set; }

    [XmlElement("X_SATIPM3U", Namespace = "urn:ses-com:satip")]
    public string? SatIpM3U { get; set; }
}