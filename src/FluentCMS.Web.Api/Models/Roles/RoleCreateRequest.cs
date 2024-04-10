using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class RoleCreateRequest
{
    [Length(3, 20)]
    [Required]
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
