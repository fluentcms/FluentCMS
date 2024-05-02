namespace FluentCMS.Web.Api.Models;

public class UserRegisterRequest
{
    [Required]
    public string Email { get; set; } = default!;

    [Required]
    public string Username { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;

    [Required]
    [Compare("Password")]
    public string ConfirmPassword { get; set; } = default!;
}
