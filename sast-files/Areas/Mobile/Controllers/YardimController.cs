using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

// Herkese acik (rol farki yok): kurulum yardimi tum kullanicilar icin gecerli.
[Area("Mobile")]
[Authorize]
public class YardimController : Controller
{
    public IActionResult Kurulum() => View();
}
