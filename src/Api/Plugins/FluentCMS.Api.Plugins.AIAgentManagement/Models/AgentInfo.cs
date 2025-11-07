namespace FluentCMS.Api.Plugins.AIAgentManagement.Models;

public class AgentInfo : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<FunctionInfo> Functions { get; set; } = [];
    public string Instructions { get; set; } = string.Empty;
}

public class FunctionInfo: AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
}

public class LlmInfo : AuditableEntity
{

}

public class ProjectInfo : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class ChatTemplate
{

}
