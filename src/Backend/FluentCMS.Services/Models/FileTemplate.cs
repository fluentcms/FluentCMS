namespace FluentCMS.Services.Models;

public class FileTemplate
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Name { get; set; } = default!;
    public string Path { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid FolderId { get; set; }
    public string Extension { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }
}
