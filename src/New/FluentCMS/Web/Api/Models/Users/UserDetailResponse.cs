namespace FluentCMS.Web.Api.Models;

public class UserDetailResponse : BaseAuditableResponse
{
    public required string Email { get; set; }
    public required string Username { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
}
