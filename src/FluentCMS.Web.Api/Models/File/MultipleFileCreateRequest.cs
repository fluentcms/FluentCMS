using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Models.File;

public class MultipleFileCreateRequest
{
    [Required]
    public IFormFile[] Files { get; set; } = [];
}
