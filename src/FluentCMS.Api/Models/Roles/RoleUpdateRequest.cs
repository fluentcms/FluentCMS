using FluentValidation;

namespace FluentCMS.Api.Models;

public class RoleUpdateRequest : RoleCreateRequest
{
    public Guid Id { get; set; }
}

public class RoleUpdateRequestValidator : AbstractValidator<RoleUpdateRequest>
{
    public RoleUpdateRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
