namespace FluentCMS.Web.Api.Models;

public class RoleRequest
{
    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}
