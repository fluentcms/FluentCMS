namespace FluentCMS.Api.Models;

public class UserChangePasswordRequest
{
    public required Guid UserId { get; set; }
    public required string OldPassword { get; set; }
    public required string NewPassword { get; set; }
}
