using FluentValidation;

namespace FluentCMS.Api.Models;

public class SiteIdRequest
{
    public Guid SiteId { get; set; }
}

public class SiteIdRequestValidator : AbstractValidator<SiteIdRequest>
{
    public SiteIdRequestValidator()
    {
        RuleFor(x => x.SiteId).NotNull().NotEmpty();
    }
}
