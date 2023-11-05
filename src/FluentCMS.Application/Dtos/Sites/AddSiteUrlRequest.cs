using FluentValidation;

namespace FluentCMS.Application.Dtos.Sites;

public class AddSiteUrlRequest
{
    public Guid SiteId { get; set; }
    public string NewUrl { get; set; } = "";
}

public class AddSiteUrlRequestValidator : AbstractValidator<AddSiteUrlRequest>
{
    public AddSiteUrlRequestValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.NewUrl).NotEmpty().MaximumLength(50);
    }
}
