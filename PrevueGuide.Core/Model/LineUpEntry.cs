namespace PrevueGuide.Core.Model;

public record LineUpEntry
{
    public string Id { get; init; }
    public string ChannelNumber { get; init; }
    public string CallSign { get; init; }
}
