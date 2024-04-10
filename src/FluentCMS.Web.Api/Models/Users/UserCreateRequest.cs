using System.ComponentModel.DataAnnotations;
using FluentCMS.Web.Api.Validation;

namespace FluentCMS.Web.Api.Models;

public class UserCreateRequest
{
    [Required]
    public string Username { get; set; } = default!;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    [Required]
    [PasswordValidator]
    public string Password { get; set; } = default!;
}
