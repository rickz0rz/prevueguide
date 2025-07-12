using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Model.Listings;
using PrevueGuide.Core.Model.Listings.Channel;
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
    }

    public bool RequiresManualUpdating => false;
    public int? PrevueChannelNumber { get; set; } = 1;

    public void Dispose()
    {
    }

    public async IAsyncEnumerable<IListing> GetEntries()
    {
        // Get the current time to determine the half-hours we're showing.
        var now = DateTime.Now;
        var startTime = Time.ClampToNextHalfHourIfTenMinutesAway(now);

        _logger.LogInformation("Fetching guide entries...");
        var guide = await GetGuide();
        _logger.LogInformation("Guide retrieved.");

        yield return new TimeBarListing(startTime);
        yield return new ImageListing("assets/images/guide-channel.png");

        if (PrevueChannelNumber.HasValue)
        {
            yield return new ChannelListing
            {
                // CallSign = "PREVUE",
                CallSign = "GUIDE",
                ChannelNumber = "1",
                FirstColumnStartTime = startTime,
                Programs =
                [
                    new Program()
                    {
                        StartTime = now.AddHours(-4),
                        EndTime = now.AddHours(4),
                        // Title = Font.FormatWithFontTokens("Before you view... %PREVUE%!"),
                        Title = "Can't decide? Check out the GUIDE!",
                        IsMovie = false,
                        Description = "",
                        IsClosedCaptioned = false,
                        IsStereo = false,
                        Rating = "",
                        Year = "2025"
                    }
                ]
            };
        }

        foreach (var guideElement in guide.RootElement.EnumerateArray())
        {
            var channelElement = guideElement.GetProperty("Channel");
            var channelListing = new ChannelListing();

            try
            {
                channelListing.CallSign = channelElement.GetProperty("CallSign").GetString();
                channelListing.ChannelNumber = channelElement.GetProperty("Number").GetString();
                channelListing.FirstColumnStartTime = startTime;
                channelListing.Programs = [];

                foreach (var airingElement in guideElement.GetProperty("Airings").EnumerateArray())
                {
                    var start = airingElement.GetProperty("Time").GetInt64();
                    var offset = DateTimeOffset.FromUnixTimeSeconds(start);
                    var startDateTime = offset.LocalDateTime; // Lock to :30 increments

                    var adjustment = startDateTime.Minute > 30
                        ? startDateTime.Minute - 30
                        : startDateTime.Minute;

                    if (adjustment > 0)
                    {
                        adjustment = adjustment >= 15
                            ? 30 - adjustment
                            : 0 - adjustment;
                    }

                    var endDateTime = startDateTime.AddSeconds(airingElement.GetProperty("Duration").GetInt32());
                    startDateTime = startDateTime.AddMinutes(adjustment);

                    var endTime =
                        startTime.AddMinutes(90); // make this dynamic depending on the width in the configuration.

                    if ((startDateTime < startTime && endDateTime > startTime && endDateTime <= endTime) ||
                        (startDateTime >= startTime && startDateTime < endTime && endDateTime > startTime &&
                         endDateTime <= endTime) ||
                        (startDateTime >= startTime && startDateTime < endTime && endDateTime > endTime) ||
                        (startDateTime < startTime && endDateTime > endTime))
                        {
                            var hasTags = airingElement.TryGetProperty("Tags", out var tags);
                            var isMovie = airingElement.TryGetProperty("MovieID", out _);

                            channelListing.Programs.Add(new Program
                            {
                                StartTime = startDateTime,
                                EndTime = endDateTime,
                                Title = airingElement.GetProperty("Title").GetString(),
                                IsMovie = isMovie,
                                Description = airingElement.TryGetProperty("Summary", out _)
                                    ? airingElement.GetProperty("Summary").GetString()
                                    : string.Empty,
                                Rating = airingElement.TryGetProperty("ContentRating", out _)
                                    ? airingElement.GetProperty("ContentRating").GetString()
                                    : string.Empty,
                                Year = isMovie
                                    ? airingElement.GetProperty("ReleaseYear").GetInt32().ToString()
                                    : string.Empty,
                                IsStereo = hasTags && tags.EnumerateArray().Any(x => x.GetString().Equals("Stereo")),
                                IsClosedCaptioned =
                                    hasTags && tags.EnumerateArray().Any(x => x.GetString().Equals("CC"))
                            });
                        }
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Unable to process channel element");
                continue;
            }

            yield return channelListing;
        }
    }

    private async Task<JsonDocument> GetGuide()
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{_address}/devices/ANY/guide");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }
}
