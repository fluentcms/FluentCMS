using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Controllers;

public class SingleFileCreateRequest
{
    [Required]
    public IFormFile File { get; set; } = default!;
}
