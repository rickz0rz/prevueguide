namespace PrevueGuide.Core.Model;

public record Listing
{
    public string ChannelId { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
    public string StartTime { get; init; }
    public string EndTime { get; init; }
}
