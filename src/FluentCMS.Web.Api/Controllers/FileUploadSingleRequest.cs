using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Controllers;

public class FileUploadSingleRequest
{
    [Required]
    public IFormFile File { get; set; } = default!;
}
