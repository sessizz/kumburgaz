using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class DocumentsControllerTests
{
    [Fact]
    public async Task Create_stores_every_valid_uploaded_file_as_a_document_attachment()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.Create(
            new DocumentRecord
            {
                Title = "Karar",
                Category = "Toplanti",
                DocumentDate = new DateTime(2026, 7, 9)
            },
            [
                CreateFile("karar.pdf", "application/pdf", [1]),
                CreateFile("katilim.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [2])
            ]);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DocumentsController.Details), redirect.ActionName);

        var attachments = await db.Attachments
            .Where(x => x.EntityType == nameof(DocumentRecord))
            .OrderBy(x => x.FileName)
            .ToListAsync();
        Assert.Equal(["karar.pdf", "katilim.xlsx"], attachments.Select(x => x.FileName));
        Assert.All(attachments, attachment => Assert.Equal(redirect.RouteValues!["id"], attachment.EntityId));
    }

    [Fact]
    public async Task PreviewFile_rejects_an_attachment_owned_by_another_document()
    {
        await using var db = CreateDb();
        var first = new DocumentRecord { Title = "Bir", Category = "Genel" };
        var second = new DocumentRecord { Title = "Iki", Category = "Genel" };
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        var attachment = new Attachment
        {
            EntityType = nameof(DocumentRecord),
            EntityId = first.Id,
            FileName = "bir.pdf",
            ContentType = "application/pdf",
            Content = [1],
            ByteSize = 1
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();

        var result = await CreateController(db).PreviewFile(second.Id, attachment.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_returns_only_the_selected_documents_attachments()
    {
        await using var db = CreateDb();
        var selected = new DocumentRecord { Title = "Secilen", Category = "Genel" };
        var other = new DocumentRecord { Title = "Diger", Category = "Genel" };
        db.AddRange(selected, other);
        await db.SaveChangesAsync();
        db.Attachments.AddRange(
            new Attachment { EntityType = nameof(DocumentRecord), EntityId = selected.Id, FileName = "secilen.pdf", ContentType = "application/pdf", Content = [1], ByteSize = 1 },
            new Attachment { EntityType = nameof(DocumentRecord), EntityId = other.Id, FileName = "diger.pdf", ContentType = "application/pdf", Content = [2], ByteSize = 1 });
        await db.SaveChangesAsync();

        var result = await CreateController(db).Details(selected.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DocumentDetailViewModel>(view.Model);
        var attachment = Assert.Single(model.Attachments);
        Assert.Equal("secilen.pdf", attachment.FileName);
    }

    [Fact]
    public async Task DownloadFile_returns_the_original_attachment_with_a_download_name()
    {
        await using var db = CreateDb();
        var document = new DocumentRecord { Title = "Tutanak", Category = "Genel" };
        db.DocumentRecords.Add(document);
        await db.SaveChangesAsync();
        var attachment = new Attachment
        {
            EntityType = nameof(DocumentRecord),
            EntityId = document.Id,
            FileName = "tutanak.pdf",
            ContentType = "application/pdf",
            Content = [5, 6],
            ByteSize = 2
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();

        var result = await CreateController(db).DownloadFile(document.Id, attachment.Id);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("tutanak.pdf", file.FileDownloadName);
        Assert.Equal(new byte[] { 5, 6 }, file.FileContents);
    }

    [Fact]
    public async Task Edit_adds_new_files_without_removing_existing_document_attachments()
    {
        await using var db = CreateDb();
        var document = new DocumentRecord { Title = "Tutanak", Category = "Genel", DocumentDate = new DateTime(2026, 7, 9) };
        db.DocumentRecords.Add(document);
        await db.SaveChangesAsync();
        db.Attachments.Add(new Attachment
        {
            EntityType = nameof(DocumentRecord),
            EntityId = document.Id,
            FileName = "eski.pdf",
            ContentType = "application/pdf",
            Content = [1],
            ByteSize = 1
        });
        await db.SaveChangesAsync();

        await CreateController(db).Edit(document, [CreateFile("yeni.png", "image/png", [2, 3])]);

        var attachments = await db.Attachments.OrderBy(x => x.FileName).ToListAsync();
        Assert.Equal(["eski.pdf", "yeni.png"], attachments.Select(x => x.FileName));
        Assert.Equal(new byte[] { 2, 3 }, attachments.Single(x => x.FileName == "yeni.png").Content);
    }

    [Fact]
    public async Task DeleteAttachment_soft_deletes_only_the_selected_document_attachment()
    {
        await using var db = CreateDb();
        var document = new DocumentRecord { Title = "Tutanak", Category = "Genel" };
        db.DocumentRecords.Add(document);
        await db.SaveChangesAsync();
        var selected = new Attachment { EntityType = nameof(DocumentRecord), EntityId = document.Id, FileName = "sil.pdf", ContentType = "application/pdf", Content = [1], ByteSize = 1 };
        var remaining = new Attachment { EntityType = nameof(DocumentRecord), EntityId = document.Id, FileName = "kal.pdf", ContentType = "application/pdf", Content = [2], ByteSize = 1 };
        db.Attachments.AddRange(selected, remaining);
        await db.SaveChangesAsync();

        var result = await CreateController(db).DeleteAttachment(document.Id, selected.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("kal.pdf", (await db.Attachments.SingleAsync()).FileName);
        var deleted = await db.Attachments.IgnoreQueryFilters().SingleAsync(x => x.Id == selected.Id);
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task Delete_hides_all_attachments_owned_by_the_document()
    {
        await using var db = CreateDb();
        var document = new DocumentRecord { Title = "Silinecek", Category = "Genel" };
        db.DocumentRecords.Add(document);
        await db.SaveChangesAsync();
        db.Attachments.AddRange(
            new Attachment { EntityType = nameof(DocumentRecord), EntityId = document.Id, FileName = "bir.pdf", ContentType = "application/pdf", Content = [1], ByteSize = 1 },
            new Attachment { EntityType = nameof(DocumentRecord), EntityId = document.Id, FileName = "iki.pdf", ContentType = "application/pdf", Content = [2], ByteSize = 1 });
        await db.SaveChangesAsync();

        await CreateController(db).Delete(document.Id);

        Assert.Null(await db.DocumentRecords.FindAsync(document.Id));
        Assert.Empty(await db.Attachments.Where(x => x.EntityId == document.Id).ToListAsync());
        var deletedAttachments = await db.Attachments.IgnoreQueryFilters()
            .Where(x => x.EntityType == nameof(DocumentRecord) && x.EntityId == document.Id)
            .ToListAsync();
        Assert.Equal(2, deletedAttachments.Count);
        Assert.All(deletedAttachments, attachment => Assert.True(attachment.IsDeleted));
    }

    private static DocumentsController CreateController(ApplicationDbContext db)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new DocumentsController(db, new DocumentFileService(), new CaptureSessionService())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
        return controller;
    }

    private static IFormFile CreateFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
