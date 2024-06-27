namespace FluentCMS.Web.Api.Models;

public class FileDetailResponse : BaseAuditableResponse
{
    public Guid FolderId { get; set; }
    public string Name { get; set; } = default!;
    public long Size { get; set; }
    public string Extension { get; set; } = default!;
    public string ContentType { get; set; } = default!;
}
