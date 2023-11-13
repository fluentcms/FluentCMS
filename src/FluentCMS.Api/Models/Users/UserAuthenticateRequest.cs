using FluentValidation;

namespace FluentCMS.Api.Models;

public class UserAuthenticateRequest
{
    public required string Username { get; set; } 
    public required string Password { get; set; } 
}

public class UserAuthenticateRequestValidator : AbstractValidator<UserAuthenticateRequest>
{
    public UserAuthenticateRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
