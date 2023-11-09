using FluentValidation;

namespace FluentCMS.Api.Models;

public class UserCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public ICollection<Guid> Roles { get; set; } = new List<Guid>();
}

public class CreateUserRequestValidator : AbstractValidator<UserCreateRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(3).MaximumLength(50);
    }
}
