namespace FluentCMS.Api.Plugins.AIAgentManagement.Repositories;

internal class AgentDataSeeder(AIDbContext dbContext, ILogger<AgentDataSeeder> logger) : BaseDataSeeder<AIDbContext>(dbContext, logger)
{
    public override int Priority => 10000;

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Seeding initial agent items into the database...");
        await DbContext.Agents.AddRangeAsync([
            new Agent
            {
                Name = "Code Companion",
                Description = "Assists with writing, refactoring, and optimizing code across multiple languages.",
                SystemPrompt = "You are an expert software engineer. Generate clean, efficient, and secure code. Avoid unnecessary comments and keep responses minimal.",
                Model = "openai/gpt-4.1"
            },
            new Agent
            {
                Name = "Debug Analyzer",
                Description = "Specializes in debugging and error diagnosis for C#, JavaScript, and Python codebases.",
                SystemPrompt = "You are a debugging assistant. Identify issues, explain causes briefly, and propose direct fixes.",
                Model = "openai/gpt-4.1-mini"
            },
            new Agent
            {
                Name = "Architecture Advisor",
                Description = "Provides guidance on software design patterns, system architecture, and scalability decisions.",
                SystemPrompt = "You are a senior software architect. Recommend architecture decisions based on scalability, maintainability, and performance.",
                Model = "openai/gpt-4.1"
            },
            new Agent
            {
                Name = "Test Writer",
                Description = "Generates and improves unit, integration, and end-to-end tests for various frameworks.",
                SystemPrompt = "You are a testing expert. Write thorough, maintainable, and minimal test code using best practices.",
                Model = "openai/gpt-4.1-mini"
            },
            new Agent
            {
                Name = "Doc Generator",
                Description = "Creates concise technical documentation, code summaries, and API references.",
                SystemPrompt = "You are a technical documentation assistant. Generate clear, professional, and accurate documentation from code or descriptions.",
                Model = "openai/gpt-4.1-nano"
            }
        ], cancellationToken);

        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
