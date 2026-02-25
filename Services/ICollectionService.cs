using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface ICollectionService
{
    Task<List<Collection>> GetAllAsync();
    Task CreateAsync(CollectionCreateViewModel model);
}
