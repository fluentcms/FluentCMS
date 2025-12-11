namespace FluentCMS.Api.Plugins.AIAgentManagement.Repositories;

internal class AgentSchemaValidator(AIDbContext dbContext, ILogger<AgentSchemaValidator> logger) : BaseSchemaValidator<AIDbContext>(dbContext, logger)
{
    public override int Priority => 10000;
}
