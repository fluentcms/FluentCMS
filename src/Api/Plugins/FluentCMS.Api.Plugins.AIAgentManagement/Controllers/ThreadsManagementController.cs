using Microsoft.AspNetCore.Mvc;
// using Microsoft.Threads.AI;
using Microsoft.Extensions.AI;

namespace FluentCMS.Api.Plugins.AIAgentManagement.Controllers;

[ApiController]
public class ThreadsManagementController : BaseController
{
    private readonly IThreadRepository _threadRepository;
    private readonly ILogger<ThreadsManagementController> _logger;

    public ThreadsManagementController(IThreadRepository threadRepository, ILogger<ThreadsManagementController> logger)
    {
        _threadRepository = threadRepository;
        _logger = logger;
    }

    private static ThreadResponseDto MapToResponseDto(AIThread thread)
    {
        return new ThreadResponseDto
        {
            Id = thread.Id,
            Name = thread.Name,
            Description = thread.Description,
            Model = thread.Model,
            SystemPrompt = thread.SystemPrompt,
        };
    }

    [HttpGet]
    public async Task<ApiListResponse<ThreadResponseDto>> GetAll(CancellationToken cancellationToken)
    {
            var threads = await _threadRepository.Query().ToList(cancellationToken);
            
            var threadResponses = threads.Select(MapToResponseDto).ToList();
            return SuccessList(threadResponses, 1, threadResponses.Count, threadResponses.Count);
        
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ThreadResponseDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var thread = await _threadRepository.Query().OrderBy(thread => thread.Id).FirstOrDefault(a => a.Id == id, cancellationToken);
        if (thread == null)
            return NotFound("AIThread not found");

        var threadResponse = MapToResponseDto(thread);
        return Success(threadResponse);
    }

    [HttpPost]
    public async Task<ActionResult<AIThread>> Create([FromBody] AIThread createThreadRequest)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var createdThread = await _threadRepository.Add(createThreadRequest);
            return CreatedAtAction(nameof(GetById), new { id = createdThread.Id }, createdThread);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating thread");
            return StatusCode(500, "An error occurred while creating the thread");
        }
    }

    [HttpPut]
    public async Task<ActionResult<AIThread>> Update([FromBody] AIThread updateThreadRequest)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var updatedThread = await _threadRepository.Update(updateThreadRequest);
            
            if (updatedThread == null)
            {
                return NotFound($"AIThread with ID {updateThreadRequest.Id} not found");
            }
            
            return Ok(updatedThread);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "An error occurred while updating the thread");
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var thread = await _threadRepository.Query().OrderBy(thread => thread.Id).FirstOrDefault(a => a.Id == id, cancellationToken);
            if (thread is null)
            {
                return NotFound($"AIThread with ID {id} not found");
            }

            var success = await _threadRepository.Remove(thread, cancellationToken);
            
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting thread with ID {Id}", id);
            return StatusCode(500, "An error occurred while deleting the thread");
        }
    }
}

public class CreateThreadRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Model { get; set; }
    public string SystemPrompt { get; set; }
}

public class UpdateThreadRequest
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
}

public class ThreadResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Model { get; set; }
    public string SystemPrompt { get; set; }
}
