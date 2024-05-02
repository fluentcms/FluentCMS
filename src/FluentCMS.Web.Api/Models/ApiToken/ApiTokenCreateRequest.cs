namespace FluentCMS.Web.Api.Models;

public class ApiTokenCreateRequest
{
    [Required]
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    public DateTime? ExpiredAt { get; set; }

    [Required]
    public bool Enabled { get; set; } = true;

    [Required]
    public List<Policy> Policies { get; set; } = [];
}
