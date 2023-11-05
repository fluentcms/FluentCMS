using FluentValidation;

namespace FluentCMS.Application.Dtos.Sites;

public class DeleteSiteRequest
{
    public Guid Id { get; set; }
}

public class DeleteSiteRequestValidator : AbstractValidator<DeleteSiteRequest>
{
    public DeleteSiteRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
