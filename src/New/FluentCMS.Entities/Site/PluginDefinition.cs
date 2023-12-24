namespace FluentCMS.Entities;

public class PluginDefinition : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string ViewType { get; set; } = default!;
    public string? EditType { get; set; }
    public string? SettingType { get; set; }
}
