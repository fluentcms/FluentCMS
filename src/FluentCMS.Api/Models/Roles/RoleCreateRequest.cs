using FluentValidation;

namespace FluentCMS.Api.Models;

public class RoleCreateRequest
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public bool AutoAssigned { get; set; } = false;
    public Guid SiteId { get; set; }
}

public class RoleCreateRequestValidator : AbstractValidator<RoleCreateRequest>
{
    public RoleCreateRequestValidator()
    {
        RuleFor(x => x.SiteId).NotNull().NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(1024).When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}
