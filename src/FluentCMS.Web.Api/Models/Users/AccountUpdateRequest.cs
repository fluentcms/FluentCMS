namespace FluentCMS.Web.Api.Models;

public class AccountUpdateRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Phone]
    public string? PhoneNumber { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
