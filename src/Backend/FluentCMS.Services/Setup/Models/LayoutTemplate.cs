namespace FluentCMS.Services.Setup.Models;

internal class LayoutTemplate
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; } = default!;
    public bool IsDefault { get; set; } = false;
}
