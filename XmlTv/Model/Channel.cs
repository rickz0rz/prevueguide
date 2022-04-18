using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace XmlTv.Model;

[XmlRoot(ElementName = "channel")]
public class Channel
{
    private Lazy<string> _callSign;
    private Lazy<string> _channelNumber;
    private Lazy<string> _sourceName;

    [XmlElement(ElementName = "display-name")]
    public List<string>? DisplayName { get; set; }
    [XmlElement(ElementName = "icon")]
    public Icon? Icon { get; set; }
    [XmlAttribute(AttributeName = "id")]
    public string? Id { get; set; }

    public string CallSign => _callSign.Value;
    public string ChannelNumber => _channelNumber.Value;
    public string SourceName => _sourceName.Value;

    public Channel()
    {
        _callSign = new Lazy<string>(new Func<string>(() =>
        {
            foreach (var component in from displayName in DisplayName
                     select displayName.Split(" ")
                     into components
                     from component in components
                     where component.ToCharArray().Any(char.IsLetter) && !component.Contains(':')
                     select component)
            {
                return component;
            }

            throw new Exception("Unable to find channel number in displayName");
        }));

        _channelNumber = new Lazy<string>(() =>
        {
            foreach (var component in from displayName in DisplayName
                     select displayName.Split(" ")
                     into components
                     from component in components
                     where component.ToCharArray().All(c => char.IsDigit(c) || c is '.' or '-')
                     select component)
            {
                return component;
            }

            throw new Exception("Unable to find channel number in displayName");
        });

        _sourceName = new Lazy<string>(() =>
        {
            if (string.IsNullOrWhiteSpace(Id))
                throw new Exception("Id not found within Channel");

            var shaM = SHA512.Create();
            var hashString = Convert.ToHexString(shaM.ComputeHash(Encoding.ASCII.GetBytes(Id)));
            return hashString.Length > 6 ? hashString[..6] : hashString;
        });
    }
}
