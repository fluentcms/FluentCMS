using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;
public class ContentCreateRequest
{
    [Required]
    public required Dictionary<string, object?> Value { get; set; }
}
