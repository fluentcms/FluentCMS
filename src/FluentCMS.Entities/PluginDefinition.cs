namespace FluentCMS.Entities;

public class PluginDefinition : AuditEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? ViewType { get; set; }
    public string? EditType { get; set; }
    public string? SettingType { get; set; }
}
