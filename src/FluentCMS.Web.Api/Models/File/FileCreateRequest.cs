using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Models;

public class FileCreateRequest
{
    public IFormFile File { get; set; }
}
