using System.ComponentModel.DataAnnotations;
using FluentCMS.Web.Api.Validation;

namespace FluentCMS.Web.Api.Models;

public class UserRegisterRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Required]
    [RegularExpression("^[a-zA-Z0-9][a-zA-Z0-9_]{4,19}$")]
    public required string Username { get; set; }

    [Required]
    [PasswordValidator]
    public required string Password { get; set; }

}
