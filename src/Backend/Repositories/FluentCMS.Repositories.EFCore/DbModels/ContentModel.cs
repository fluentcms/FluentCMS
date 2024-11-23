namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Contents")]
public class ContentModel : SiteAssociatedEntityModel
{
    public Guid ContentTypeId { get; set; }
    public ICollection<ContentDataModel> Data { get; set; } = [];
}
