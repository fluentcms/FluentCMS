namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("Contents")]
public class ContentModel : SiteAssociatedEntityModel
{
    public Guid TypeId { get; set; }
    public string Data { get; set; } = default!;
}
