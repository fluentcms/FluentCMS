namespace FluentCMS.Web.Api.Setup.Models;
internal class PageSectionTemplate
{
    public Dictionary<string, string> Styles { get; set; } = [];
    public List<PageRowTemplate> Rows { get; set; } = [];
}

