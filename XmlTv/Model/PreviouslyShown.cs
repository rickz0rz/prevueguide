using System.Xml.Serialization;

namespace XmlTv.Model;

[XmlRoot(ElementName = "previously-shown")]
public class PreviouslyShown
{
    [XmlAttribute(AttributeName = "start")]
    public string? Start { get; set; }
}
