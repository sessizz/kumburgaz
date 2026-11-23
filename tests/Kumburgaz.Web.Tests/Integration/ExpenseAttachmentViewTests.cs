using System.Net;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

// Gider ekleri resim ya da belge olabilir. PDF bir <img> ile çizilince tarayıcı kırık resim
// gösterir (ekranda yalnız dosya adı görünür); belgeler dosya kutucuğu olarak çizilmeli ve
// tıklanınca türüne göre açılmalı.
[Collection(IntegrationCollection.Name)]
public class ExpenseAttachmentViewTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";
    private readonly OAuthTestClient _oauth = new(factory);

    [Fact]
    public async Task Desktop_expense_list_draws_pdf_as_file_tile_and_photo_as_image()
    {
        var (_, pdfId, photoId) = await SeedExpenseWithAttachmentsAsync();
        var browser = await LoginAsAdminAsync();

        var html = await browser.GetStringAsync("/Ledger");

        Assert.DoesNotContain($"<img src=\"/Ledger/Ek/{pdfId}\"", html);
        Assert.Contains($"data-attachment-url=\"/Ledger/Ek/{pdfId}\"", html);
        Assert.Contains("data-attachment-type=\"application/pdf\"", html);
        Assert.Contains($"<img src=\"/Ledger/Ek/{photoId}\"", html);
    }

    [Fact]
    public async Task Desktop_expense_edit_opens_attachments_by_type()
    {
        var (ledgerId, pdfId, _) = await SeedExpenseWithAttachmentsAsync();
        var browser = await LoginAsAdminAsync();

        var html = await browser.GetStringAsync($"/Ledger/Edit/{ledgerId}");

        Assert.Contains($"data-attachment-url=\"/Ledger/Ek/{pdfId}\"", html);
        Assert.Contains("data-attachment-type=\"application/pdf\"", html);
    }

    [Fact]
    public async Task Mobile_expense_detail_draws_pdf_as_file_tile()
    {
        var (ledgerId, pdfId, photoId) = await SeedExpenseWithAttachmentsAsync();
        var browser = await LoginAsAdminAsync();

        var html = await browser.GetStringAsync($"/m/Gider/Detay/{ledgerId}");

        Assert.DoesNotContain($"<img src=\"/m/Gider/Ek/{pdfId}\"", html);
        Assert.Contains($"data-attachment-url=\"/m/Gider/Ek/{pdfId}\"", html);
        Assert.Contains("data-attachment-type=\"application/pdf\"", html);
        Assert.Contains($"<img src=\"/m/Gider/Ek/{photoId}\"", html);
    }

    [Fact]
    public async Task Both_layouts_load_the_attachment_viewer()
    {
        var browser = await LoginAsAdminAsync();

        Assert.Contains("/js/attachment-viewer.js", await browser.GetStringAsync("/Ledger"));
        Assert.Contains("/js/attachment-viewer.js", await browser.GetStringAsync("/m/Gider"));
    }

    [Fact]
    public async Task Desktop_pdf_tile_has_thumbnail_canvas_and_fallback()
    {
        var (_, pdfId, _) = await SeedExpenseWithAttachmentsAsync();
        var browser = await LoginAsAdminAsync();

        var html = await browser.GetStringAsync("/Ledger");

        AssertPdfTileHasThumbnail(html, $"/Ledger/Ek/{pdfId}");
    }

    [Fact]
    public async Task Mobile_pdf_tile_has_thumbnail_canvas_and_fallback()
    {
        var (ledgerId, pdfId, _) = await SeedExpenseWithAttachmentsAsync();
        var browser = await LoginAsAdminAsync();

        var html = await browser.GetStringAsync($"/m/Gider/Detay/{ledgerId}");

        AssertPdfTileHasThumbnail(html, $"/m/Gider/Ek/{pdfId}");
    }

    [Theory]
    [InlineData("/lib/pdfjs/pdf.min.mjs")]
    [InlineData("/lib/pdfjs/pdf.worker.min.mjs")]
    public async Task Pdfjs_files_are_served_locally(string path)
    {
        var browser = await LoginAsAdminAsync();

        var response = await browser.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Contains(contentType, new[] { "text/javascript", "application/javascript" });
    }

    // PDF kutucuğu: data-pdf-thumb işaretli düğme, içinde canvas + yedek ikon + dosya adı.
    private static void AssertPdfTileHasThumbnail(string html, string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html,
            "<button[^>]*data-attachment-url=\"" + System.Text.RegularExpressions.Regex.Escape(url) + "\"[^>]*>.*?</button>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(match.Success, "PDF kutucuğu bir düğme olarak bulunamadı.");
        var tile = match.Value;

        Assert.Contains("data-pdf-thumb", tile);
        Assert.Contains("<canvas", tile);
        Assert.Contains("data-pdf-fallback", tile);
        Assert.Contains("Enpara işlem dekontunuz.pdf", System.Net.WebUtility.HtmlDecode(tile));
    }

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var userName = $"gider-{Guid.NewGuid():N}";
        await _oauth.CreateUserAsync(userName, Password, AppRoles.SistemYonetici);
        var browser = _oauth.CreateBrowser();
        var login = await OAuthTestClient.LoginAsync(browser, userName, Password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return browser;
    }

    private async Task<(int LedgerId, int PdfId, int PhotoId)> SeedExpenseWithAttachmentsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new IncomeExpenseCategory { Name = $"Şemsiye ve Masa {Guid.NewGuid():N}", Type = "Gider", Active = true };
        db.IncomeExpenseCategories.Add(category);
        await db.SaveChangesAsync();

        var ledger = new LedgerTransaction
        {
            Date = DateTime.UtcNow.Date,
            IncomeExpenseCategoryId = category.Id,
            Amount = 50_000m,
            PaymentChannel = PaymentChannel.Bank,
            Description = "100 ADET SANDALYE ÖDEMESİ"
        };
        db.LedgerTransactions.Add(ledger);
        await db.SaveChangesAsync();

        var pdf = new Attachment
        {
            EntityType = nameof(LedgerTransaction),
            EntityId = ledger.Id,
            FileName = "Enpara işlem dekontunuz.pdf",
            ContentType = "application/pdf",
            ByteSize = 8,
            Content = "%PDF-1.4"u8.ToArray()
        };
        var photo = new Attachment
        {
            EntityType = nameof(LedgerTransaction),
            EntityId = ledger.Id,
            FileName = "fis.jpg",
            ContentType = "image/jpeg",
            ByteSize = 3,
            Content = [0xFF, 0xD8, 0xFF]
        };
        db.Attachments.AddRange(pdf, photo);
        await db.SaveChangesAsync();

        return (ledger.Id, pdf.Id, photo.Id);
    }
}
