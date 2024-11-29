namespace FluentCMS.Services.Models;

public class PageModel : Page
{
    public string FullPath { get; set; } = string.Empty;
    public List<PageModel> Children { get; set; } = [];
}
