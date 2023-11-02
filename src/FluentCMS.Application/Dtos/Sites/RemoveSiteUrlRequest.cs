namespace FluentCMS.Application.Dtos.Sites;
public class RemoveSiteUrlRequest
{
    public Guid SiteId { get; set; }
    public string Url { get; set; } = "";
}
