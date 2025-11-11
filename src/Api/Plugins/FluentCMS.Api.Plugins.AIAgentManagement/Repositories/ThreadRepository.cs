using FluentCMS.Api.Plugins.AIAgentManagement.Models;

namespace FluentCMS.Api.Plugins.AIAgentManagement.Repositories;


public interface IThreadRepository : IRepository<AIThread>
{
}

internal class ThreadRepository(AIDbContext dbContext) : Repository<AIThread, AIDbContext>(dbContext), IThreadRepository;
