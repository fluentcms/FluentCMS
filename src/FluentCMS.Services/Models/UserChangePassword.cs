namespace FluentCMS.Services.Models;

public class UserChangePassword
{
    public Guid UserId { get; set; }
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}
