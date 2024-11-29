namespace FluentCMS.Services.Models;

public class FolderTemplate
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid? ParentId { get; set; }
}
