namespace FluentCMS.Web.Api.Models;

public class UserUpdateRequest
{
    [Required]
    public required Guid Id { get; set; }

    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    [Required]
    public bool Enabled { get; set; }

    public bool Locked { get; set; }
}
