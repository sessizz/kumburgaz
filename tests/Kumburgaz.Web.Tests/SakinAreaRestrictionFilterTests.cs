using System.Security.Claims;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class SakinAreaRestrictionFilterTests
{
    private static AuthorizationFilterContext CreateContext(string role, string method, params object[] endpointMetadata)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"))
        };
        httpContext.Request.Method = method;
        var actionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata };
        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), actionDescriptor), []);
    }

    [Fact]
    public async Task Sakin_get_on_desktop_action_redirects_to_mobile()
    {
        var context = CreateContext(AppRoles.Sakin, HttpMethods.Get);

        await new SakinAreaRestrictionFilter().OnAuthorizationAsync(context);

        Assert.Equal("/m", Assert.IsType<RedirectResult>(context.Result).Url);
    }

    [Fact]
    public async Task Sakin_post_on_desktop_action_is_forbidden()
    {
        var context = CreateContext(AppRoles.Sakin, HttpMethods.Post);

        await new SakinAreaRestrictionFilter().OnAuthorizationAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
    }

    // OAuth yetkilendirme ucu Sakin'i /m'e değil, istemciye access_denied ile döndürür.
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Sakin_is_not_restricted_on_endpoints_marked_to_skip(string method)
    {
        var context = CreateContext(AppRoles.Sakin, method, new SkipSakinRestrictionAttribute());

        await new SakinAreaRestrictionFilter().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Staff_user_is_not_restricted()
    {
        var context = CreateContext(AppRoles.MuhasebeGorevli, HttpMethods.Get);

        await new SakinAreaRestrictionFilter().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }
}
