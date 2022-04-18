using System.Xml.Serialization;

namespace XmlTv.Model;

[XmlRoot(ElementName = "audio")]
public class Audio
{
    [XmlElement(ElementName = "stereo")]
    public string? Stereo { get; set; }
}
