namespace FluentCMS.Web.Api.Setup.Models;
internal class PageRowTemplate
{
    public Dictionary<string, string> Styles { get; set; } = [];
    public List<PageColumnTemplate> Columns { get; set; } = [];
}

