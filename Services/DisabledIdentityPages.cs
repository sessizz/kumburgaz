using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Identity Default UI'ın kendi kendine hesap açma sayfalarını kapatır. Bu sayfalar
/// [AllowAnonymous] taşıdığı için AuthorizeAreaPage kuralı onları korumuyordu ve
/// /Identity/Account/Register herkese açıktı. Kullanıcılar Sistem Kullanıcıları
/// ekranından (SystemUsersController) açılır.
/// </summary>
public static class DisabledIdentityPages
{
    public static readonly IReadOnlyList<string> Pages =
    [
        "/Account/Register",
        "/Account/RegisterConfirmation",
        "/Account/ExternalLogin",
        "/Account/ResendEmailConfirmation",
    ];

    public static RazorPagesOptions DisableSelfRegistration(this RazorPagesOptions options)
    {
        foreach (var page in Pages)
        {
            options.Conventions.AddAreaPageApplicationModelConvention("Identity", page,
                model => model.Filters.Add(new NotFoundResourceFilter()));
        }

        return options;
    }

    // Kaynak filtresi: sayfa modeli hiç oluşturulmadan 404 döner.
    private sealed class NotFoundResourceFilter : IAsyncResourceFilter
    {
        public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }
    }
}
