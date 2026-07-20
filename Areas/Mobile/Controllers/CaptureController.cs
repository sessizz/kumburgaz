using System.Security.Claims;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

/// <summary>
/// Telefon tarafi: masaustunde baslatilan bir yakalama oturumuna (token) fotograf/dosya
/// yukler. Push bildirimine dokunarak veya QR okutarak buraya gelinir. Yetki: sadece
/// [Authorize] - oturum, baslatan kullanicinin UserId'siyle eslesmezse gorunmez.
/// </summary>
[Area("Mobile")]
[Authorize]
public class CaptureController(
    CaptureSessionService sessions,
    ImageAttachmentService imageAttachmentService,
    DocumentFileService documentFileService) : Controller
{
    [HttpGet]
    public IActionResult Index(string token)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var session = sessions.Get(token, userId);
        if (session is null)
        {
            return View(new CaptureIndexViewModel { IsValid = false, Token = token ?? string.Empty });
        }

        var files = sessions.ListFiles(token, userId);
        return View(new CaptureIndexViewModel
        {
            IsValid = true,
            Token = token!,
            Purpose = session.Purpose,
            Files = files.Select(f => new CaptureStagedFileSummary { Id = f.Id, FileName = f.FileName }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Yukle(string token, List<IFormFile>? files)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var session = sessions.Get(token, userId);
        if (session is null)
        {
            return NotFound();
        }

        var uploaded = (files ?? []).Where(f => f.Length > 0).ToList();
        if (uploaded.Count == 0)
        {
            TempData["ActionError"] = "Bir dosya seçin.";
            return RedirectToAction(nameof(Index), new { token });
        }

        var added = 0;
        foreach (var file in uploaded)
        {
            try
            {
                // Amaca degil dosya uzantisina gore dallanir: "gider" oturumunda resimler
                // hala sikistirilir (fis fotografi davranisi), ama artik pdf/docx/xlsx gibi
                // belge turleri de (her iki amacta) DocumentFileService ile kabul edilir.
                CaptureAddFileResult result;
                var extension = Path.GetExtension(file.FileName);
                if (session.Purpose == "gider" && ImageAttachmentService.SupportedExtensions.Contains(extension))
                {
                    var compressed = await imageAttachmentService.CompressAsync(file);
                    result = sessions.AddFile(token, userId, compressed.FileName, compressed.ContentType, compressed.Content);
                }
                else
                {
                    var validated = await documentFileService.ValidateAsync(file);
                    if (!validated.IsValid)
                    {
                        TempData["ActionError"] = validated.ErrorMessage;
                        continue;
                    }

                    result = sessions.AddFile(token, userId, validated.FileName, validated.ContentType, validated.Content);
                }

                switch (result)
                {
                    case CaptureAddFileResult.Success:
                        added++;
                        break;
                    case CaptureAddFileResult.TooManyFiles:
                        TempData["ActionError"] = $"Bu oturumda en fazla {CaptureSessionService.MaxFilesPerSession} dosya ekleyebilirsiniz.";
                        break;
                    case CaptureAddFileResult.SessionTooLarge:
                        TempData["ActionError"] = "Bu oturum için boyut sınırına ulaşıldı. Masaüstünde formu kaydedip yeni bir oturum başlatın.";
                        break;
                    case CaptureAddFileResult.SessionNotFound:
                        TempData["ActionError"] = "Bağlantının süresi doldu. Masaüstünde tekrar deneyin.";
                        break;
                }
            }
            catch (Exception ex)
            {
                TempData["ActionError"] = ex.Message;
            }
        }

        if (added > 0)
        {
            TempData["ActionSuccess"] = added == 1 ? "Dosya eklendi." : $"{added} dosya eklendi.";
        }

        return RedirectToAction(nameof(Index), new { token });
    }
}
