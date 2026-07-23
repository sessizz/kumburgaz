using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface ICollectionService
{
    Task<List<Collection>> GetAllAsync();
    Task<Collection?> GetByIdAsync(int id);
    Task<int> CreateAsync(CollectionCreateViewModel model);
    Task UpdateAsync(int id, CollectionCreateViewModel model);
    Task DeleteAsync(int id);
}
