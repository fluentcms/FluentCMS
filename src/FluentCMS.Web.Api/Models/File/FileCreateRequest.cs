using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Models.File;

public class FileCreateRequest
{
    public IFormFile File { get; set; } = null;
}
