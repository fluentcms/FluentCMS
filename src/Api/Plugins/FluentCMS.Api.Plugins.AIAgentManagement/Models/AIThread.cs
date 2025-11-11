namespace FluentCMS.Api.Plugins.AIAgentManagement.Models;

public class AIThread : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Model { get; set; }
    public string SystemPrompt { get; set; }
}
