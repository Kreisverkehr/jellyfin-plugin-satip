using System.Xml.Serialization;
using Kreisverkehr.NetUpnp.Model;

namespace Kreisverkehr.Jellyfin.Plugin.SatIp.Upnp;

[XmlRoot("root", Namespace = "urn:schemas-upnp-org:device-1-0")]
public class SatIpDescription : UpnpDescription
{
    [XmlElement("device", Namespace = "urn:schemas-upnp-org:device-1-0", Type = typeof(SatIpDevice))]
    public override required UpnpDevice Device { get => base.Device; set => base.Device = value; }
}