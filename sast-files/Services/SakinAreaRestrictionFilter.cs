using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Sakin rolündeki kullanıcıların masaüstü (Mobile/Identity dışı) controller'lara erişimini
/// gerçek bir sunucu sınırı ile engeller. GET istekleri /m'e yönlendirilir, yazma istekleri 403 alır.
/// Böylece masaüstü controller'lara ayrıca veri kapsamı eklemek gerekmez.
/// </summary>
public sealed class SakinAreaRestrictionFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole(AppRoles.Sakin))
        {
            return Task.CompletedTask;
        }

        var area = context.RouteData.Values["area"]?.ToString();
        if (string.Equals(area, "Mobile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(area, "Identity", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (HttpMethods.IsGet(context.HttpContext.Request.Method)
            || HttpMethods.IsHead(context.HttpContext.Request.Method))
        {
            context.Result = new RedirectResult("/m");
        }
        else
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }

        return Task.CompletedTask;
    }
}
