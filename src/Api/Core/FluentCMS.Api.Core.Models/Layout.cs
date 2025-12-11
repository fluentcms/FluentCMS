namespace FluentCMS.Api.Core.Models;

public class Layout : SiteAssociatedEntity
{
    public Guid SiteId { get; set; }
    public string Name { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Head { get; set; } = default!;
}
