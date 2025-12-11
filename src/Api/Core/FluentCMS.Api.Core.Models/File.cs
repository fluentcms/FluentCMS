namespace FluentCMS.Api.Core.Models;

public class File : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid FolderId { get; set; }
    public Guid SiteId { get; set; }
    public string Extension { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }
}
