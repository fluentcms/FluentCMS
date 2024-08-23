namespace FluentCMS.Web.Api.Models;

public class UserDetailResponse : BaseAuditableResponse
{
    public string Username { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime? LoginAt { get; set; }
    public int LoginCount { get; set; }
    public bool Enabled { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneConfirmed { get; set; }
    public bool Locked { get; set; }
}
