using FluentValidation;

namespace FluentCMS.Api.Models;

public class UpdateSiteRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid RoleId { get; set; }
    public ICollection<string> Urls { get; set; } = new List<string>();
}

public class UpdateSiteRequestValidator : AbstractValidator<UpdateSiteRequest>
{
    public UpdateSiteRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Description).MaximumLength(100).When(x => string.IsNullOrWhiteSpace(x.Description) == false);
        RuleFor(x => x.Urls).NotNull().Must(x => x.Any());
    }
}
