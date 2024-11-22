namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("ContentTypeFieldSettings")]
public class ContentTypeFieldSettingsModel : EntityModel
{
    public Guid ContentTypeFieldId { get; set; } // Foreign key
    public string Key { get; set; } = default!; // key for the dictionary
    public string Value { get; set; } = default!; // json string value
    public ContentTypeFieldModel ContentTypeField { get; set; } = default!; // Navigation property
}
