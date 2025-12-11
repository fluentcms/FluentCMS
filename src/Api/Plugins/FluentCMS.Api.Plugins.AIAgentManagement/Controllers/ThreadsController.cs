using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FluentCMS.Api.Plugins.AIAgentManagement.Controllers;

public class ThreadsController(OpenRouterClient openRouterClient, Tools tools) : BaseController
{
    [HttpPost]
    public async Task<ApiResponse<AgentRunResponse>> Start(CancellationToken cancellationToken = default)
    {
        var chatClient = openRouterClient.GetChatClient();
        AIAgent agent = chatClient.CreateAIAgent(instructions: "You are a senior full-stack software developer",
            tools: [
                AIFunctionFactory.Create(tools.ListFiles),
        AIFunctionFactory.Create(tools.WriteFile),
        AIFunctionFactory.Create(tools.ReadFile)]);


        var path = "C:\\Projects\\microsoft-agent\\ConsoleApp1";
        var instructions = $"""
    You are a senior full-stack software developer.
    Your task is to review and understand the source code files in the given path: {path}.
    After reviewing the files, you need to create a comprehensive README.md file in markdown format that explains the source code.
    The README.md file should include:
    1. An overview of the project.
    2. A brief description of each source code file and its purpose.
    3. Any important classes, methods, or functions and their roles.
    4. Instructions on how to set up and run the project if applicable.
    Please write the markdown content in {path}\\README.md file into the root folder
    """;
        // Non-streaming agent interaction with function tools.
        var agentResponse = await agent.RunAsync(instructions, cancellationToken: cancellationToken);
        return Success(agentResponse);
    }


}

//public class ThreadDto
//{
//    public Guid? Id { get; set; }
//    public Guid AgentId { get; set; }
//    public Guid TemplateId { get; set; }
//    public string Message { get; set; }
//}

//public class ThreadResponseDto: AgentRunResponse
//{
//    public Guid Id { get; set; }
//    public Guid TemplateId { get; set; }
//}
