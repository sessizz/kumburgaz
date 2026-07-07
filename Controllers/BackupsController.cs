using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
public class BackupsController(BackupService backupService, IWebHostEnvironment environment) : Controller
{
    public IActionResult Index()
    {
        var files = backupService.ListBackups().ToList();
        return View(new BackupIndexViewModel
        {
            Directory = backupService.BackupDirectory,
            Files = files,
            LastBackupAt = files.OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.CreatedAt
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create()
    {
        try
        {
            var path = await backupService.CreateBackupAsync("manuel");
            TempData["ActionSuccess"] = $"Yedek alındı: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            TempData["ActionError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    public IActionResult Download(string fileName)
    {
        var path = backupService.ResolveBackupPath(fileName);
        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ActionError"] = "Geri yüklenecek yedek dosyası seçin.";
            return RedirectToAction(nameof(Index));
        }

        var tempDir = Path.Combine(environment.ContentRootPath, "App_Data", "RestoreUploads");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}-{Path.GetFileName(file.FileName)}");

        try
        {
            await using (var stream = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(stream);
            }

            await backupService.RestoreAsync(tempPath);
            TempData["ActionSuccess"] = "Yedek geri yüklendi.";
        }
        catch (Exception ex)
        {
            TempData["ActionError"] = ex.Message;
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }

        return RedirectToAction(nameof(Index));
    }
}
