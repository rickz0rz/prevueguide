using Guide.Core.Model.Listings;

namespace Guide.Core.Data;

public interface IListingsDataProvider : IDisposable
{
    IAsyncEnumerable<IListing> GetEntries();
}
