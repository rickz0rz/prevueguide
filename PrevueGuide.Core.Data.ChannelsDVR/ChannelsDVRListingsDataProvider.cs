
using System.Text.Json;
using PrevueGuide.Core.Model;

namespace PrevueGuide.Core.Data.ChannelsDVR;

public class ChannelsDVRListingsDataProvider : IListingsDataProvider
{
    private readonly string _address;

    public bool RequiresManualUpdating => false;

    private async Task<JsonDocument> GetChannels()
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{_address}/api/v1/channels");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private async Task<JsonDocument> GetGuide()
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{_address}/devices/ANY/guide");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    public ChannelsDVRListingsDataProvider(string address)
    {
        _address = address;
        GetChannels().Wait();
    }

    public void Dispose()
    {
    }

    public Task AddChannelToLineup(string id, string channelNumber, string callSign)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<LineUpEntry>> GetChannelLineup()
    {
        var list = new List<LineUpEntry>();
        var stationIds = new List<string>();
        var channels = await GetChannels();

        foreach (var channelElement in channels.RootElement.EnumerateArray())
        {
            if (channelElement.TryGetProperty("station_id", out var stationElement))
            {
                var stationId = stationElement.GetString();

                if (stationIds.Contains(stationId))
                    continue;

                stationIds.Add(stationId);

                var lineUpEntry = new LineUpEntry
                {
                    CallSign = channelElement.GetProperty("name").GetString(),
                    ChannelNumber = channelElement.GetProperty("number").GetString(),
                    Id = channelElement.GetProperty("station_id").GetString()
                };

                list.Add(lineUpEntry);
            }
            else
            {
                Console.WriteLine("Oof: " + channelElement);
            }
        }

        return list;
    }

    public Task AddChannelListing(List<(string channelId, string title, string category, string description, string year, string rating, string subtitled, DateTime startTime, DateTime endTime)> listings)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime)
    {
        var listings = new List<Listing>();
        var guide = await GetGuide();

        foreach (var guideElement in guide.RootElement.EnumerateArray())
        {
            var airingsElement = guideElement.GetProperty("Airings");

            var channelElement = guideElement.GetProperty("Channel");

            foreach (var airingElement in airingsElement.EnumerateArray())
            {
                var start = airingElement.GetProperty("Time").GetInt64();
                var offset = DateTimeOffset.FromUnixTimeSeconds(start);
                var startDateTime = offset.LocalDateTime;
                var duration = airingElement.GetProperty("Duration").GetInt32();
                var endDateTime = startDateTime.AddSeconds(duration);

                if ((startDateTime < startTime && endDateTime > startTime && endDateTime <= endTime) ||
                    (startDateTime >= startTime && startDateTime < endTime && endDateTime > startTime && endDateTime <= endTime) ||
                    (startDateTime >= startTime && startDateTime < endTime && endDateTime > endTime) ||
                    (startDateTime < startTime && endDateTime > endTime))
                {
                    try
                    {
                        var listing = new Listing
                        {
                            StartTime = startDateTime,
                            EndTime = endDateTime,
                            Title = airingElement.GetProperty("Title").GetString(),
                            ChannelId = channelElement.GetProperty("Station").ToString(),
                            Block = Utilities.Time.CalculateBlockNumber(startDateTime)
                        };

                        listings.Add(listing);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Oof: " + airingElement);
                    }
                }
            }
        }

        return listings;
    }
}
