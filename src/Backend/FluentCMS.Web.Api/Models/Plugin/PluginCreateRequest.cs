namespace FluentCMS.Web.Api.Models;

public class PluginCreateRequest
{
    public Guid DefinitionId { get; set; }
    public Guid PageId { get; set; }
    public string Title { get; set; } = default!;
    public int Order { get; set; } = 0;
    public int Cols { get; set; } = 0;
    public int ColsMd { get; set; } = 0;
    public int ColsLg { get; set; } = 0;
    public string Section { get; set; } = default!;
    public Dictionary<string, string> Settings { get; set; } = [];
}
