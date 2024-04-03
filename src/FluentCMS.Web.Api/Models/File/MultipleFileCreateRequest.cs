using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models.File;

public class MultipleFileCreateRequest
{
    [Required]
    public IFormFile[] Files { get; set; } = [];
}
