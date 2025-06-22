using PrevueGuide.Core.Model;

namespace PrevueGuide.Core.Data.LocalMemory;

public class LocalMemoryListingsDataProvider : IListingsDataProvider
{
    public bool RequiresManualUpdating => true;

    public void Dispose()
    {
        // throw new NotImplementedException();
    }

    public Task AddChannelToLineup(string id, string channelNumber, string callSign)
    {
        // throw new NotImplementedException();
        return Task.CompletedTask;
    }

    public Task<IEnumerable<LineUpEntry>> GetChannelLineup()
    {
        // throw new NotImplementedException();
        return Task.FromResult<IEnumerable<LineUpEntry>>(new List<LineUpEntry>());
    }

    public Task AddChannelListing(List<(string channelId, string title, string category, string description, string year, string rating, string subtitled, DateTime startTime, DateTime endTime)> listings)
    {
        // throw new NotImplementedException();
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime)
    {
        // throw new NotImplementedException();
        return Task.FromResult<IEnumerable<Listing>>(new List<Listing>());
    }
}
