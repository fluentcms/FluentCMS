namespace FluentCMS.Repositories.EFCore.DbModels;

public class Content : SiteAssociatedEntity
{
    public Guid TypeId { get; set; }
    public ICollection<ContentData> ContentData { get; set; } = [];
}
