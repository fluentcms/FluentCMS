namespace FluentCMS.Api.Models;

public class UserDetailResponse
{
    public required Guid Id { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string LastUpdatedBy { get; set; }
    public required DateTime LastUpdatedAt { get; set; }

    public required string Email { get; set; }
    public required string Username { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
}
