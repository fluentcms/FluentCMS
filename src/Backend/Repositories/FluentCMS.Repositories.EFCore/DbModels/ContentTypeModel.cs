namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("ContentTypes")]
public class ContentTypeModel : SiteAssociatedEntityModel
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public ICollection<ContentTypeFieldModel> Fields { get; set; } = [];
}
