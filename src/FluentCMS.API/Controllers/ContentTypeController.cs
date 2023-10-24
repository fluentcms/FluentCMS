using FluentCMS.API.DTO;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.API.Controllers;

public class ContentTypeController : BaseController
{
    private static readonly string[] Summaries = new[]
       {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

    private readonly ILogger<ContentTypeController> _logger;

    public ContentTypeController(ILogger<ContentTypeController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IEnumerable<ContentTypeDTO> Get()
    {
        return Enumerable.Range(1, 5).Select(index => new ContentTypeDTO
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })
        .ToArray();
    }
}
