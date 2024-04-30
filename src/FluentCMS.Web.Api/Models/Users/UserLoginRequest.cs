using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

public class UserLoginRequest
{
    [Required]
    public required string Username { get; set; }

    [Required]
    public required string Password { get; set; }

    public bool RememberMe { get; set; }
}
