using System.Xml.Serialization;

namespace XmlTv.Model;

[XmlRoot(ElementName = "rating")]
public class Rating
{
    [XmlElement(ElementName = "value")]
    public List<string>? Value { get; set; }
}
