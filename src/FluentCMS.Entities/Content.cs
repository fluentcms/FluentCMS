namespace FluentCMS.Entities;

public class Content : Dictionary<string, object?>, IAuditEntity, IAuthorizeEntity
{
    public Guid Id { get; set; } = default!;
    public string CreatedBy { get; set; } = string.Empty; // UserName
    public DateTime CreatedAt { get; set; }
    public string LastUpdatedBy { get; set; } = string.Empty; // UserName
    public DateTime LastUpdatedAt { get; set; }
    public Guid TypeId { get; set; }
    public Guid SiteId { get; set; }

}

public class ContentType : AuditEntity
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public IEnumerable<ContentTypeField> Fields { get; set; } = [];
}

public class ContentTypeField
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string? Placeholder { get; set; }
    public string? Hint { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; }
}
public class ContentField
{
    public string Name { get; set; } = default!;
    public string Value { get; set; } = default!;
}
