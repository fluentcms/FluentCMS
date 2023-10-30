using MediatR;

namespace FluentCMS.Application.Sites;

public class RemoveUrlSiteCommand : IRequest
{
    public Guid Id { get; set; }
    public string Url { get; set; } = "";
}
