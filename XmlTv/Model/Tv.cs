using System.Xml.Serialization;

namespace XmlTv.Model;

[XmlRoot("tv")]
public record Tv
{
    [XmlElement(ElementName="channel")]
    public List<Channel>? Channel { get; init; }
    [XmlElement(ElementName="programme")]
    public List<Programme>? Programme { get; init; }
    [XmlAttribute(AttributeName="source-info-url")]
    public string? SourceInfoUrl { get; init; }
    [XmlAttribute(AttributeName="source-info-name")]
    public string? SourceInfoName { get; init; }
    [XmlAttribute(AttributeName="generator-info-name")]
    public string? GeneratorInfoName { get; init; }
    [XmlAttribute(AttributeName="generator-info-url")]
    public string? GeneratorInfoUrl { get; init; }
}
