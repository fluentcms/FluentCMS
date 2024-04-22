using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class UserRegisterRequest
{
    [Required]
    public required string Email { get; set; }

    [Required]
    public required string Username { get; set; }

    [Required]
    public required string Password { get; set; }

    [Required]
    [Compare("Password")]
    public required string ConfirmPassword { get; set; }
}
