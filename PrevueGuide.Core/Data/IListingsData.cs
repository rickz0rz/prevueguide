using PrevueGuide.Core.Model;

namespace PrevueGuide.Core.Data;

public interface IListingsData : IDisposable
{
    Task AddChannelToLineup(string id, string channelNumber, string callSign);
    Task<IEnumerable<(string id, string channelNumber, string callSign)>> GetChannelLineup();

    Task AddChannelListing(string channelId, string title, string description,
        DateTime startTime, DateTime endTime);
    Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime);
}
