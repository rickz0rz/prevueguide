using PrevueGuide.Core.Model;

namespace PrevueGuide.Core.Data;

public interface IListingsDataProvider : IDisposable
{
    public bool RequiresManualUpdating { get; }

    public Task AddChannelToLineup(string id, string channelNumber, string callSign);
    public Task<IEnumerable<LineUpEntry>> GetChannelLineup();

    public Task AddChannelListing(List<(string channelId, string title, string category, string description, string year,
        string rating, string subtitled, DateTime startTime, DateTime endTime)> listings);

    public Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime);
}
