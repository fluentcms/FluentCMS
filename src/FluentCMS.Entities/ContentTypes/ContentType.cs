namespace FluentCMS.Entities;

public class ContentType : AuditEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}
