using System.Xml.Serialization;

namespace XmlTv.Model;

[XmlRoot(ElementName = "subtitles")]
public class Subtitles
{
    [XmlAttribute(AttributeName = "type")]
    public string? Type { get; set; }
}
