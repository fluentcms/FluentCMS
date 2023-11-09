namespace FluentCMS.Entities;

public class ContentType : AuditEntity
{
    public string Title { get; protected set; } = string.Empty;
    public string Slug { get; protected set; } = string.Empty;
}
