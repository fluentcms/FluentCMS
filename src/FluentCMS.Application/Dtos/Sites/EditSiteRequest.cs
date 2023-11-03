using FluentValidation;

namespace FluentCMS.Application.Dtos.Sites;

public class EditSiteRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid RoleId { get; set; }
    public ICollection<string> URLs { get; set; } = new List<string>();
}

public class EditSiteRequestValidator : AbstractValidator<EditSiteRequest>
{
    public EditSiteRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Description).MaximumLength(100).When(x => string.IsNullOrWhiteSpace(x.Description) == false);
        RuleFor(x => x.URLs).NotNull().Must(x => x.Any());
    }
}
