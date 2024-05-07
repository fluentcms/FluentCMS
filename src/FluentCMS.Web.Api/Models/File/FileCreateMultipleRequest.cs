using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Models;

public class FileCreateMultipleRequest
{
    public IFormFile[] Files { get; set; }
}
