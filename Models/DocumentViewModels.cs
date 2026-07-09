namespace Kumburgaz.Web.Models;

public class DocumentAttachmentSummary
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int ByteSize { get; set; }
}

public class DocumentDetailViewModel
{
    public DocumentRecord Document { get; set; } = new();
    public List<DocumentAttachmentSummary> Attachments { get; set; } = [];
}
