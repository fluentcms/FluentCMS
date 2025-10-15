namespace FluentCMS.Api.Core.Models;

public class Folder : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public Guid? ParentId { get; set; }
}
