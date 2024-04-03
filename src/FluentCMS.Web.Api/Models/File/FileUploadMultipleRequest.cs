using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models.File;

public class FileUploadMultipleRequest
{
    [Required]
    public IFormFile[] Files { get; set; } = [];
}
