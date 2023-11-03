namespace FluentCMS.Entities;

public class ContentType : AuditEntity
{
    public required string Title { get; set; }
    public required string Slug { get; set; }
}

public class ContentTypeField
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Label { get; set; }
    public required bool Hidden { get; set; }
    public string? DefaultValue { get; set; }
    public required FieldType FieldType { get; set; }
}

public enum FieldType
{
    Text, Number, Date, DateTime, Boolean
}