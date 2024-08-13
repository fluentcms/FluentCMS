namespace FluentCMS.Web.Api.Models;

public class PluginUpdateRequest
{
    public Guid Id { get; set; }
    public int Order { get; set; } = 0;
    public string Section { get; set; } = default!;
    public int Cols { get; set; } = 0;
    public int ColsMd { get; set; } = 0;
    public int ColsLg { get; set; } = 0;
    public Dictionary<string, string> Settings { get; set; } = [];
}
