using System.Security.Claims;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kumburgaz.Web.Controllers;

public sealed record ConnectedAppRow(string Id, string ClientName, DateTimeOffset? CreatedAt, bool Active, string? OwnerName);

// Kullanıcının Claude gibi dış uygulamalara verdiği izinler; buradan iptal edilir.
[Authorize]
public class ConnectedAppsController(
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictApplicationManager applications,
    IOpenIddictTokenManager tokens,
    UserManager<ApplicationUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var isAdmin = User.IsInRole(AppRoles.SistemYonetici);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var rows = new List<ConnectedAppRow>();
        var ownerNames = new Dictionary<string, string?>();
        var source = isAdmin
            ? authorizations.ListAsync(null, null, ct)
            : authorizations.FindBySubjectAsync(userId ?? "", ct);

        // Önce belleğe: ListAsync satırları akıtır ve Npgsql, okuyucu açıkken döngüdeki sorguları reddeder.
        foreach (var authorization in await source.ToListAsync(ct))
        {
            if (await authorizations.GetTypeAsync(authorization, ct) != AuthorizationTypes.Permanent) continue;

            var applicationId = await authorizations.GetApplicationIdAsync(authorization, ct);
            var application = applicationId is null ? null : await applications.FindByIdAsync(applicationId, ct);
            var clientName = application is null ? "(silinmiş uygulama)"
                : await applications.GetDisplayNameAsync(application, ct) ?? "(adsız)";

            string? ownerName = null;
            if (isAdmin)
            {
                var subject = await authorizations.GetSubjectAsync(authorization, ct) ?? "";
                if (!ownerNames.TryGetValue(subject, out ownerName))
                {
                    var owner = await users.FindByIdAsync(subject);
                    ownerName = owner?.UserName;
                    ownerNames[subject] = ownerName;
                }
            }

            rows.Add(new ConnectedAppRow(
                await authorizations.GetIdAsync(authorization, ct) ?? "",
                clientName,
                await authorizations.GetCreationDateAsync(authorization, ct),
                await authorizations.GetStatusAsync(authorization, ct) == Statuses.Valid,
                ownerName));
        }

        return View(rows.OrderByDescending(r => r.CreatedAt).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        var authorization = await authorizations.FindByIdAsync(id, ct);
        if (authorization is null) return NotFound();

        var subject = await authorizations.GetSubjectAsync(authorization, ct);
        var isOwner = subject is not null && subject == User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!isOwner && !User.IsInRole(AppRoles.SistemYonetici)) return NotFound();

        await authorizations.TryRevokeAsync(authorization, ct);
        await tokens.RevokeByAuthorizationIdAsync(id, ct);

        TempData["ActionSuccess"] = "Bağlantı iptal edildi.";
        return RedirectToAction(nameof(Index));
    }
}
