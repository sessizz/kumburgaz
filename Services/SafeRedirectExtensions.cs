using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Services;

public static class SafeRedirectExtensions
{
    /// <summary>
    /// returnUrl yalnızca bu siteye ait bir yol ise ona, değilse <paramref name="fallbackUrl"/>'e döner.
    /// Doğrulanmamış <c>Redirect(returnUrl)</c> kullanıcıyı başka bir siteye (oltalama) yönlendirebilir.
    /// </summary>
    public static IActionResult RedirectToLocal(this Controller controller, string? returnUrl, string fallbackUrl)
    {
        var target = !string.IsNullOrWhiteSpace(returnUrl) && controller.Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : fallbackUrl;
        return controller.LocalRedirect(target);
    }
}
