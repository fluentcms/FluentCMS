using FluentValidation;

namespace FluentCMS.Application.Dtos.Users;

public class EditUserRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public ICollection<Guid> Roles { get; set; } = new List<Guid>();
}

public class EditUserRequestValidator : AbstractValidator<EditUserRequest>
{
    public EditUserRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Password).MinimumLength(3).MaximumLength(50)
            .When(x => string.IsNullOrWhiteSpace(x.Password) == false);
    }
}
