namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("ContentTypeFields")]
public class ContentTypeFieldModel : EntityModel
{
    public Guid ContentTypeId { get; set; } = default!; // Foreign key
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool Required { get; set; }
    public bool Unique { get; set; }
    public string Label { get; set; } = default!;
    public ContentTypeModel ContentType { get; set; } = default!; // Navigation property
    public ICollection<ContentTypeFieldSettingsModel> Settings { get; set; } = []; // Navigation property
}
