using PrevueGuide.Core.Model.Listings;

namespace PrevueGuide.Core.Data;

public interface IListingsDataProvider : IDisposable
{
    IAsyncEnumerable<IListing> GetEntries();
}
