using FluentValidation;

namespace FluentCMS.Application.Dtos.Sites;

public class RemoveSiteUrlRequest
{
    public Guid SiteId { get; set; }
    public string Url { get; set; } = "";
}

public class RemoveSiteUrlRequestValidator : AbstractValidator<RemoveSiteUrlRequest>
{
    public RemoveSiteUrlRequestValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.Url).NotEmpty().MaximumLength(50);
    }
}
