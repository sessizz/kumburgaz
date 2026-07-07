using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
public class SettingsController : Controller
{
    public IActionResult Index() => View();
}
