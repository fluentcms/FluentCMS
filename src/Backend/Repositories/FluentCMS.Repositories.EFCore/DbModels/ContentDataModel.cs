namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("ContentData")]
public class ContentDataModel : EntityModel
{
    public Guid ContentId { get; set; } // foreign key
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!; // JSON string
    public ContentModel Content { get; set; } = default!; // navigation property
}
