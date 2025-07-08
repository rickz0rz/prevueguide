using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.Utilities;

namespace PrevueGuide.Core.Data.ChannelsDVR;

public class ChannelsDVRListingsDataProvider : IListingsDataProvider
{
    private readonly ILogger _logger;
    private readonly string _address;

    public ChannelsDVRListingsDataProvider(ILogger logger, string address)
    {
        _logger = logger;
        _address = address;

        GetChannels().Wait();
    }

    public bool RequiresManualUpdating => false;
    public int? PrevueChannelNumber { get; set; } = 1;

    public void Dispose()
    {
    }

    public Task AddChannelToLineup(string id, string channelNumber, string callSign)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<LineUpEntry>> GetChannelLineup()
    {
        var lineUpEntries = new List<LineUpEntry>();
        var stationIds = new List<string>();

        _logger.LogInformation("Fetching lineup entries...");
        var channels = await GetChannels();
        _logger.LogInformation("Lineup retrieved.");

        if (PrevueChannelNumber.HasValue)
        {
            lineUpEntries.Add(new LineUpEntry
            {
                CallSign = "PREVUE",
                ChannelNumber = $"{PrevueChannelNumber.Value}",
                Id = "PREVUE"
            });
        }

        foreach (var channelElement in channels.RootElement.EnumerateArray())
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

                lineUpEntries.Add(lineUpEntry);
            }
            else
            {
                Console.WriteLine(": " + channelElement);
            }

        return lineUpEntries;
    }

    public Task AddChannelListing(
        List<(string channelId, string title, string category, string description, string year, string rating, string
            subtitled, DateTime startTime, DateTime endTime)> listings)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime)
    {
        var listings = new List<Listing>();

        _logger.LogInformation("Fetching guide entries...");
        var guide = await GetGuide();
        _logger.LogInformation("Guide retrieved.");

        if (PrevueChannelNumber.HasValue)
        {
            listings.Add(new Listing
            {
                StartTime = DateTime.Now.AddHours(-4),
                EndTime = DateTime.Now.AddHours(4),
                Title = Font.FormatWithFontTokens("Before you view... %PREVUE%!"),
                ChannelId = "PREVUE",
                Block = Time.CalculateBlockNumber(DateTime.Now)
            });
        }

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
                    try
                    {
                        var channelId = channelElement.GetProperty("ChannelID").GetString();

                        if (channelElement.TryGetProperty("Station", out var stationElement))
                        {
                            channelId = stationElement.GetString();
                        }

                        var listing = new Listing
                        {
                            StartTime = startDateTime,
                            EndTime = endDateTime,
                            Title = Font.FormatWithFontTokens(FormatTitle(airingElement)),
                            ChannelId = channelId,
                            Block = Time.CalculateBlockNumber(startDateTime)
                        };

                        listings.Add(listing);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Unable to process airing element");
                    }
            }
        }

        return listings;
    }

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

    private string FormatTitle(JsonElement airingElement)
    {
        var titleValue = airingElement.GetProperty("Title").GetString();
        var isMovie = airingElement.TryGetProperty("MovieID", out _);
        var title = isMovie
            ? titleValue.Split("\"", StringSplitOptions.RemoveEmptyEntries).First().Split("(").First().Trim()
                .Replace("%", "%%")
            : titleValue.Replace("%", "%%");
        var summary = airingElement.TryGetProperty("Summary", out _)
            ? " " + airingElement.GetProperty("Summary").GetString().Replace("%", "%%")
            : string.Empty;

        var foundRating = airingElement.TryGetProperty("ContentRating", out _)
            ? airingElement.GetProperty("ContentRating").GetString()
            : string.Empty;
        var rating = !string.IsNullOrWhiteSpace(foundRating) ? $" %{foundRating.Replace("-", "")}%" : "";

        var stereo = string.Empty;
        var closedCaptioning = string.Empty;

        if (airingElement.TryGetProperty("Tags", out var tagsProperty))
        {
            stereo = tagsProperty.EnumerateArray().Any(x => x.GetString().Equals("Stereo"))
                ? " %STEREO%"
                : string.Empty;

            closedCaptioning =
                tagsProperty.EnumerateArray().Any(x => x.GetString().Equals("CC"))
                    ? " %CC%"
                    : string.Empty;
        }

        var movieReleaseYear = isMovie
            ? airingElement.GetProperty("ReleaseYear").GetInt32().ToString()
            : string.Empty;

        var extraString = isMovie
            ? $"{rating}{summary}{stereo}{closedCaptioning}"
            : $"{rating}{stereo}{closedCaptioning}".Trim();
        extraString = string.IsNullOrWhiteSpace(extraString) ? string.Empty : $" {extraString}".TrimEnd();

        var generatedDescription = isMovie
            ? $"\"{title.Trim()}\" ({movieReleaseYear}){extraString}"
            : $"{title.Trim()}{extraString}";

        return generatedDescription;
    }
}
