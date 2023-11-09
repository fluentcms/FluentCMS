using FluentValidation;

namespace FluentCMS.Api.Models;

public class RoleUpdateRequest
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required bool AutoAssigned { get; set; }
    public Guid? SiteId { get; set; }
}

public class RoleUpdateRequestValidator : AbstractValidator<RoleUpdateRequest>
{
    public RoleUpdateRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(100).When(x => string.IsNullOrWhiteSpace(x.Description) == false);
    }
}
