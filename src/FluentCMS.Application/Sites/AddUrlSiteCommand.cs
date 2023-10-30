using MediatR;

namespace FluentCMS.Application.Sites;

public class AddUrlSiteCommand : IRequest
{
    public Guid Id { get; set; }
    public string NewUrl { get; set; } = "";
}
