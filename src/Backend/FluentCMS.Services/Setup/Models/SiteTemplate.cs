namespace FluentCMS.Services.Setup.Models;

internal class SiteTemplate
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string? Layout { get; set; } = default!;
    public string? EditLayout { get; set; } = default!;
    public string? DetailLayout { get; set; } = default!;
}
