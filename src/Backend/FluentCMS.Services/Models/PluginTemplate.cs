namespace FluentCMS.Services.Models;

public class PluginTemplate
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public string Definition { get; set; } = default!;
    public string Section { get; set; } = default!;
    public string Type { get; set; } = default!;
    public int Cols { get; set; } = 12;
    public int ColsMd { get; set; }
    public int ColsLg { get; set; }
    public List<Dictionary<string, object?>> Content { get; set; } = default!;
    public string? ContentPath { get; set; } = default!;
    public Dictionary<string, string> Settings { get; set; } = [];
    public bool Locked { get; set; } = false;
    public List<string> AdminRoles { get; set; } = [];
    public List<string> ContributorRoles { get; set; } = [];
    public List<string> ViewRoles { get; set; } = [];
}
