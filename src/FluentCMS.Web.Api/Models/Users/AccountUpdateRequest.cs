namespace FluentCMS.Web.Api.Models;

public class AccountUpdateRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
