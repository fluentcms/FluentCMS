namespace FluentCMS.Entities;

public class Settings : AuditableEntity
{
    // The Id property is Id of the entity that these settings belong to
    public Dictionary<string, string> Values { get; set; } = default!;
}
