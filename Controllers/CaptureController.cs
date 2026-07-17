using System.Security.Claims;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace Kumburgaz.Web.Controllers;

/// <summary>
/// Masaustu tarafi: "Telefondan ekle" panelinin destekledigi uc noktalar.
/// Yetki, modul bazli degil - oturum her zaman baslatan kullanicinin UserId'siyle
/// eslestirilir (CaptureSessionService.Get). Gercek gider/belge yetkisi zaten
/// LedgerController/DocumentsController uzerinde ModuleAuthorize ile korunuyor.
/// </summary>
[Authorize]
public class CaptureController(CaptureSessionService sessions, NotificationService notificationService) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Baslat(string purpose)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var normalizedPurpose = purpose == "belge" ? "belge" : "gider";
        var session = sessions.Create(userId, normalizedPurpose);

        var captureUrl = Url.Action("Index", "Capture", new { area = "Mobile", token = session.Token },
            protocol: Request.Scheme, host: Request.Host.Value)!;

        var title = normalizedPurpose == "belge" ? "Belge için dosya ekle" : "Gider fişi için fotoğraf ekle";
        await notificationService.NotifyAsync(
            userId,
            NotificationType.Sistem,
            title,
            "Telefonunuzdan eklemek için dokunun.",
            $"/m/Capture?token={session.Token}");

        var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(captureUrl, QRCodeGenerator.ECCLevel.Q);
        var qrBytes = new PngByteQRCode(qrData).GetGraphic(8);
        var qrDataUri = "data:image/png;base64," + Convert.ToBase64String(qrBytes);

        return Json(new { token = session.Token, captureUrl, qrDataUri });
    }

    [HttpGet]
    public IActionResult Durum(string token)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var files = sessions.ListFiles(token, userId);

        return Json(new
        {
            files = files.Select(f => new
            {
                id = f.Id,
                fileName = f.FileName,
                url = Url.Action(nameof(Dosya), new { token, id = f.Id })
            })
        });
    }

    [HttpGet]
    public IActionResult Dosya(string token, Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var file = sessions.GetFile(token, userId, id);
        if (file is null)
        {
            return NotFound();
        }

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DosyaSil(string token, Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        sessions.RemoveFile(token, userId, id);
        return Ok();
    }
}
