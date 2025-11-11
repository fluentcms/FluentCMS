using FluentCMS.Api.Plugins.AIAgentManagement.Models;

namespace FluentCMS.Api.Plugins.AIAgentManagement.Repositories;


public interface IAgentRepository : IRepository<Agent>
{
}

internal class AgentRepository(AIDbContext dbContext) : Repository<Agent, AIDbContext>(dbContext), IAgentRepository;
