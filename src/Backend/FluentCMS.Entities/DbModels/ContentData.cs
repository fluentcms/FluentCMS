namespace FluentCMS.Repositories.EFCore.DbModels;

public class ContentData : Entity
{
    public Guid ContentId { get; set; } // foreign key
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!; // JSON string
    public Content Content { get; set; } = default!; // navigation property
}
