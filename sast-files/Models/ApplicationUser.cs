using Microsoft.AspNetCore.Identity;
namespace Kumburgaz.Web.Models;
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public string? Title { get; set; }
    public int? AccountId { get; set; }
    public Account? Account { get; set; }
}
