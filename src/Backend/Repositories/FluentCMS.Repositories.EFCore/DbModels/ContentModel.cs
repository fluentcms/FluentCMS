namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Contents")]
public class ContentModel : SiteAssociatedEntityModel
{
    public Guid TypeId { get; set; }
    public ICollection<ContentDataModel> Data { get; set; } = [];
    public ContentTypeModel Type { get; set; } = default!; // Navigation property
}
