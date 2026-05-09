using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class SettingsController : Controller
{
    public IActionResult Index() => View();
}
