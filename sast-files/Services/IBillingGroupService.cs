using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface IBillingGroupService
{
    Task<List<BillingGroup>> GetAllAsync();
    Task<BillingGroup?> GetByIdAsync(int id);
    Task CreateOrUpdateAsync(BillingGroupFormViewModel model);
    Task DeleteAsync(int id);
}
