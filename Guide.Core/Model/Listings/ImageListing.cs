namespace Guide.Core.Model.Listings;

public class ImageListing(string filename) : IListing
{
    public string Filename { get; set; } = filename;
}
