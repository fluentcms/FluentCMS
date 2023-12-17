using FluentCMS.Entities;

namespace FluentCMS.Api.Models;

public class PluginResponse
{
    public Guid Id { get; set; }
    public PluginDefinition Definition { get; set; } = default!;
    public int Order { get; set; } = 0;
    public string Section { get; set; } = default!;
}
