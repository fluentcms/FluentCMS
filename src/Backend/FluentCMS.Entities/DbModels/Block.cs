namespace FluentCMS.Repositories.EFCore.DbModels;

public class Block : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string? Description { get; set; }
    public string Content { get; set; } = default!;
}
