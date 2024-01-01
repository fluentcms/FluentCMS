namespace FluentCMS.Entities;

public class Layout : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Head { get; set; } = default!;
    public bool IsDefault { get; set; } = false;
}
