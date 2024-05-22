namespace FluentCMS.Web.Api.Models;

public class UserCreateRequest
{
    [Required]
    public string Username { get; set; } = default!;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;

    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    [Required]
    public bool Enabled { get; set; }

    [Required]
    public List<Guid> RoleIds { get; set; } = [];
}
