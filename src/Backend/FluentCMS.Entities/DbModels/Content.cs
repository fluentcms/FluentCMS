namespace FluentCMS.Repositories.EFCore.DbModels;

public class Content : SiteAssociatedEntity
{
    public Guid TypeId { get; set; }
    public Dictionary<string, object?> Data { get; set; } = [];
}
