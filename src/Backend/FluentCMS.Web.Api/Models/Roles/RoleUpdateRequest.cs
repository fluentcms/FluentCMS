namespace FluentCMS.Web.Api.Models;

public class RoleUpdateRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}
