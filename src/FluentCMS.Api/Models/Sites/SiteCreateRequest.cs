using FluentValidation;

namespace FluentCMS.Api.Models;

public class SiteCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Urls { get; set; } = [];
    public Guid RoleId { get; set; }
}

public class CreateSiteRequestValidator : AbstractValidator<SiteCreateRequest>
{
    public CreateSiteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Description).MaximumLength(100).When(x => string.IsNullOrWhiteSpace(x.Description) == false);
        RuleFor(x => x.Urls).NotNull().Must(x => x.Any());
    }
}
