using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kumburgaz.Web.Services;

// Bir controller'ı yetki matrisindeki bir modüle bağlar.
// GET/HEAD/OPTIONS istekleri "görüntüleme", diğerleri (POST/PUT/DELETE) "yazma" yetkisi ister.
// Böylece her aksiyonu ayrı etiketlemeye gerek kalmaz; gör/yazma ayrımı HTTP metodundan gelir.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ModuleAuthorizeAttribute(string module) : Attribute, IAsyncAuthorizationFilter
{
    public string Module { get; } = module;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // [AllowAnonymous] varsa atla.
        if (context.Filters.Any(f => f is IAllowAnonymousFilter))
        {
            return Task.CompletedTask;
        }

        var user = context.HttpContext.User;
        if (user.Identity is null || !user.Identity.IsAuthenticated)
        {
            context.Result = new ChallengeResult();
            return Task.CompletedTask;
        }

        var permissions = context.HttpContext.RequestServices.GetRequiredService<PermissionService>();
        var method = context.HttpContext.Request.Method;
        var needWrite = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

        if (!permissions.HasAccess(user, Module, needWrite))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }

        return Task.CompletedTask;
    }
}
