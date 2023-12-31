namespace FluentCMS.Web.Api.Models;

public class UserDetailResponse : BaseAuditableResponse
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
}
