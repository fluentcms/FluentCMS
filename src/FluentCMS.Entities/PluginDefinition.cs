namespace FluentCMS.Entities;

public class PluginDefinition : AuditEntity, IAuthorizeEntity
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? ViewType { get; set; }
    public string? EditType { get; set; }
    public string? SettingType { get; set; }
}
