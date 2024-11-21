namespace FluentCMS.Repositories.EFCore.DbModels;

public class ContentTypeField : Entity
{
    public Guid ContentTypeId { get; set; } = default!; // Foreign key
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool Required { get; set; }
    public bool Unique { get; set; }
    public string Label { get; set; } = default!;
    public ContentType ContentType { get; set; } = default!; // Navigation property
    public ICollection<ContentTypeFieldSettings> Settings { get; set; } = []; // Navigation property
}

public class ContentTypeFieldSettings : Entity
{
    public Guid ContentTypeFieldId { get; set; } // Foreign key
    public string Key { get; set; } = default!; // key for the dictionary
    public string Value { get; set; } = default!; // json string value
    public ContentTypeField ContentTypeField { get; set; } = default!; // Navigation property
}
