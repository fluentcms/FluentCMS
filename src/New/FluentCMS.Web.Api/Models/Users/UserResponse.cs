namespace FluentCMS.Web.Api.Models;

public class UserResponse : BaseAuditableResponse
{
    public string UserName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }
}
