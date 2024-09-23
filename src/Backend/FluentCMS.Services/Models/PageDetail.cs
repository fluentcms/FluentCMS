namespace FluentCMS.Services.Models;

public class PageDetail : Page
{
    public string FullPath { get; set; } = default!;
    public List<PageDetail> Children { get; set; } = [];
}
