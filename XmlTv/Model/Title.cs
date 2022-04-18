using System.Xml.Serialization;

namespace XmlTv.Model;

[XmlRoot(ElementName = "title")]
public record Title
{
    [XmlAttribute(AttributeName = "lang")]
    public string? Lang { get; set; }
    [XmlText]
    public string? Text { get; set; }
}
