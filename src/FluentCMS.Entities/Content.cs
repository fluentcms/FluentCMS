namespace FluentCMS.Entities;

public class Content : Dictionary<string, object?>, IAuditEntity, IAuthorizeEntity
{
    public Guid Id { get; set; } = default!;
    public string CreatedBy { get; set; } = string.Empty; // UserName
    public DateTime CreatedAt { get; set; }
    public string LastUpdatedBy { get; set; } = string.Empty; // UserName
    public DateTime LastUpdatedAt { get; set; }
    public string Type { get; set; }
    public Guid SiteId { get; set; }

    public static Content FromDictionary(Dictionary<string, object?> dict)
    {
        var content = new Content();

        // Iterate over the dictionary and map each key-value pair to the corresponding property
        foreach (var kvp in dict)
        {
            switch (kvp.Key)
            {
                case nameof(Id):
                    content.Id = Guid.Parse(kvp.Value?.ToString() ?? Guid.Empty.ToString());
                    break;
                case nameof(CreatedBy):
                    content.CreatedBy = kvp.Value?.ToString() ?? string.Empty;
                    break;
                case nameof(CreatedAt):
                    content.CreatedAt = DateTime.TryParse(kvp.Value?.ToString(), out DateTime createdAt) ? createdAt : default;
                    break;
                case nameof(LastUpdatedBy):
                    content.LastUpdatedBy = kvp.Value?.ToString() ?? string.Empty;
                    break;
                case nameof(LastUpdatedAt):
                    content.LastUpdatedAt = DateTime.TryParse(kvp.Value?.ToString(), out DateTime lastUpdatedAt) ? lastUpdatedAt : default;
                    break;
                case nameof(Type):
                    content.Type = kvp.Value?.ToString() ?? string.Empty;
                    break;
                case nameof(SiteId):
                    content.SiteId = Guid.Parse(kvp.Value?.ToString() ?? Guid.Empty.ToString());
                    break;
                default:
                    content.Add(kvp.Key, kvp.Value);
                    break;
            }
        }

        return content;
    }

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
