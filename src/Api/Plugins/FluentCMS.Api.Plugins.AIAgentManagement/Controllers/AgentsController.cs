using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FluentCMS.Api.Plugins.AIAgentManagement.Controllers;

[ApiController]
public class AgentsController : BaseController
{
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(IAgentRepository agentRepository, ILogger<AgentsController> logger)
    {
        _agentRepository = agentRepository;
        _logger = logger;
    }

    private static AgentResponseDto MapToResponseDto(Agent agent)
    {
        return new AgentResponseDto
        {
            Id = agent.Id,
            Name = agent.Name,
            Description = agent.Description,
            Model = agent.Model,
            SystemPrompt = agent.SystemPrompt,
        };
    }

    [HttpGet]
    public async Task<ApiListResponse<AgentResponseDto>> GetAll(CancellationToken cancellationToken)
    {
            var agents = await _agentRepository.Query().ToList(cancellationToken);
            
            var agentResponses = agents.Select(MapToResponseDto).ToList();
            return SuccessList(agentResponses, 1, agentResponses.Count, agentResponses.Count);
        
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AgentResponseDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var agent = await _agentRepository.Query().OrderBy(agent => agent.Id).FirstOrDefault(a => a.Id == id, cancellationToken);
        if (agent == null)
            return NotFound("Agent not found");

        var agentResponse = MapToResponseDto(agent);
        return Success(agentResponse);
    }

    [HttpPost]
    public async Task<ActionResult<Agent>> Create([FromBody] Agent createAgentRequest)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var createdAgent = await _agentRepository.Add(createAgentRequest);
            return CreatedAtAction(nameof(GetById), new { id = createdAgent.Id }, createdAgent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating agent");
            return StatusCode(500, "An error occurred while creating the agent");
        }
    }

    [HttpPut]
    public async Task<ActionResult<Agent>> Update([FromBody] Agent updateAgentRequest)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var updatedAgent = await _agentRepository.Update(updateAgentRequest);
            
            if (updatedAgent == null)
            {
                return NotFound($"Agent with ID {updateAgentRequest.Id} not found");
            }
            
            return Ok(updatedAgent);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "An error occurred while updating the agent");
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var agent = await _agentRepository.Query().OrderBy(agent => agent.Id).FirstOrDefault(a => a.Id == id, cancellationToken);
            if (agent is null)
            {
                return NotFound($"Agent with ID {id} not found");
            }

            var success = await _agentRepository.Remove(agent, cancellationToken);
            
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent with ID {Id}", id);
            return StatusCode(500, "An error occurred while deleting the agent");
        }
    }
}


public class CreateAgentRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Model { get; set; }
    public string SystemPrompt { get; set; }
}

public class UpdateAgentRequest
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
}

public class AgentResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Model { get; set; }
    public string SystemPrompt { get; set; }
}
