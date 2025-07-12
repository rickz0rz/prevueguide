namespace PrevueGuide.Core.Model.Listings;

public class TimeBarListing(DateTime startTime) : IListing
{
    public DateTime StartTime { get; set; } = startTime;
}
