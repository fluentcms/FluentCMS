using FluentValidation;

namespace FluentCMS.Api.Models;

public class UserUpdateRequest
{
    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public ICollection<Guid> RoleIds { get; set; } = [];
}

public class UserUpdateRequestValidator : AbstractValidator<UserUpdateRequest>
{
    public UserUpdateRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
