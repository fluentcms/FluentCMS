using MediatR;

namespace FluentCMS.Application.Sites;

public class DeleteSiteCommand : IRequest<Guid>
{
    public Guid Id { get; set; }
}
