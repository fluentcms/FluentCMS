using FluentValidation;

namespace FluentCMS.Api.Models;

public class UserChangePasswordRequest
{
    public required Guid UserId { get; set; }
    public required string OldPassword { get; set; }
    public required string NewPassword { get; set; }
}

public class UserChangePasswordRequestValidator : AbstractValidator<UserChangePasswordRequest>
{
    public UserChangePasswordRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OldPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty();
    }
}
