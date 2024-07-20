namespace FluentCMS.Web.Api.Models;

public class PageSectionCreateRequest
{
    public Guid PageId { get; set; }
    public int Order { get; set; }
    public Dictionary<string, string> Styles { get; set; } = [];
}
