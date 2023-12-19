namespace FluentCMS.Entities;

public class App : AppAssociatedEntity
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
}
