using FluentValidation;

namespace FluentCMS.Application.Dtos.Users;

public class DeleteUserRequest
{
    public Guid Id { get; set; }
}

public class DeleteUserRequestValidator : AbstractValidator<DeleteUserRequest>
{
    public DeleteUserRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
