namespace PrevueGuide.Core.Model.Listings.Channel;

// Display a channel listing: call sign, number, and the shows.
public class ChannelListing : IListing
{
    public string ChannelNumber { get; set; }
    public string CallSign { get; set; }
    public DateTime FirstColumnStartTime { get; set; }
    public List<Program> Programs { get; set; }
}
