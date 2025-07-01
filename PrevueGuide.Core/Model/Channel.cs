namespace PrevueGuide.Core.Model;

public record Channel
{
    public string ChannelNumber { get; init; }
    public string CallSign { get; init; }
}
