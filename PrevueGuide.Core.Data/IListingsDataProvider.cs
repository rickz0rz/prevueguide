﻿using PrevueGuide.Core.Model;

namespace PrevueGuide.Core.Data;

public interface IListingsDataProvider : IDisposable
{
    Task AddChannelToLineup(string id, string channelNumber, string callSign);
    Task<IEnumerable<LineUpEntry>> GetChannelLineup();

    Task AddChannelListing(List<(string channelId, string title, string category, string description,
        string year, string rating, string subtitled, DateTime startTime, DateTime endTime)> listings);

    Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime);
}
