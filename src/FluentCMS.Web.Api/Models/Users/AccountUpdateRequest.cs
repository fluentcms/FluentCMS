namespace FluentCMS.Web.Api.Controllers;

public class AccountUpdateRequest
{
    public required string Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
