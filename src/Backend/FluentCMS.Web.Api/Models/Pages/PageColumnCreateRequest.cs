namespace FluentCMS.Web.Api.Models;

public class PageColumnCreateRequest
{
    public Guid SectionId { get; set; }
    public int Order { get; set; }
    public Dictionary<string, string> Styles { get; set; } = [];
}
