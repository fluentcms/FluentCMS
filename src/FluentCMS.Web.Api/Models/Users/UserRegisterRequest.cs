using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

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

    public class PasswordValidatorAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not string) return new ValidationResult("Only string Value is accepted");
            var password = (string)value;
            var passwordValidator = validationContext.GetRequiredService<IPasswordValidator<User>>();
            var userManager = validationContext.GetRequiredService<UserManager<User>>();
            var result = passwordValidator.ValidateAsync(userManager, null, password).Result;
            if (!result.Succeeded)
            {
                return new ValidationResult("InvalidPassword", result.Errors.Select(x => x.Description))
            }
            return ValidationResult.Success;
        }
    }
}
