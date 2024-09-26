namespace FluentCMS.Services.Models;

public class PluginOrder
{
    public Guid Id { get; set; }
    public required string Section { get; set; } = default!;
}
