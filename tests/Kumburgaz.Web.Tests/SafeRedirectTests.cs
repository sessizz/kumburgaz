using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class SafeRedirectTests
{
    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil.example")]
    public void External_return_url_falls_back_to_the_local_default(string returnUrl)
    {
        var controller = CreateController();

        var result = controller.RedirectToLocal(returnUrl, "/Units/Detail/5");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Units/Detail/5", redirect.Url);
    }

    [Fact]
    public void Local_return_url_is_used()
    {
        var controller = CreateController();

        var result = controller.RedirectToLocal("/CashBank/Detail?key=bank-1", "/Units/Detail/5");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/CashBank/Detail?key=bank-1", redirect.Url);
    }

    [Fact]
    public void Missing_return_url_uses_the_default()
    {
        var controller = CreateController();

        var result = controller.RedirectToLocal(null, "/Dues");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Dues", redirect.Url);
    }

    private static Controller CreateController()
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new TestController { Url = new UrlHelper(actionContext) };
    }

    private sealed class TestController : Controller;
}
