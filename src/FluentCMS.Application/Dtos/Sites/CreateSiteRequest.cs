using FluentValidation;

namespace FluentCMS.Application.Dtos.Sites;

public class CreateSiteRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] URLs { get; set; } = [];
    public Guid RoleId { get; set; }
}

public class CreateSiteRequestValidator : AbstractValidator<CreateSiteRequest>
{
    public CreateSiteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Description).MaximumLength(100).When(x => string.IsNullOrWhiteSpace(x.Description) == false);
        RuleFor(x => x.URLs).NotNull().Must(x => x.Any());
    }
}
