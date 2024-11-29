namespace FluentCMS.Web.Api.Models;

public class ApiTokenUpdateRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    [Required]
    public bool Enabled { get; set; } = true;

    [Required]
    public List<Policy> Policies { get; set; } = [];

    public string? Description { get; set; }
    public DateTime? ExpireAt { get; set; }
}
