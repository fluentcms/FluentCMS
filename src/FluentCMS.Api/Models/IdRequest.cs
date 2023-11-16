using FluentValidation;

namespace FluentCMS.Api.Models;

public class IdRequest : SiteIdRequest
{
    public Guid Id { get; set; }
}

public class IdRequestValidator : AbstractValidator<IdRequest>
{
    public IdRequestValidator()
    {
        RuleFor(x => x.Id).NotNull().NotEmpty();
        RuleFor(x => x.SiteId).NotNull().NotEmpty();
    }
}
