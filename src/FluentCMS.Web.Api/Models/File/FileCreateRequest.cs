using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Models.File;

public class FileCreateRequest
{
    public string? Slug { get; set; } = null;
    public IFormFile File { get; set; } = null;
}
