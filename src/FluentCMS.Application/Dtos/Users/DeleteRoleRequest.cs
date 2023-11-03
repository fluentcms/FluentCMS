using FluentValidation;

namespace FluentCMS.Application.Dtos.Users;

public class DeleteRoleRequest
{
    public required Guid Id { get; set; }
}

public class DeleteRoleRequestValidator : AbstractValidator<DeleteRoleRequest>
{
    public DeleteRoleRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
