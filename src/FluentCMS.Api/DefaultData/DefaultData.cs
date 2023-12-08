using FluentCMS.Entities;

namespace FluentCMS.Api;

public class DefaultData
{
    public required Host Host { get; set; }
    public required DefaultUser SuperAdmin { get; set; }
    public required DefaultSite Site { get; set; }
    public required List<DefaultPage> Pages { get; set; }
    public required List<PluginDefinition> PluginDefinitions { get; set; }
    public required List<Layout> Layouts { get; set; }

    public class DefaultUser
    {
        public required string UserName { get; set; }
        public required string Email { get; set; }
        public required string Password { get; set; }
    }

    public class DefaultSite
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public required List<string> Urls { get; set; }
        public string Layout { get; set; } = default!;

        public Site GetSite() => new()
        {
            Name = Name,
            Description = Description,
            Urls = Urls
        };
    }

    public class DefaultPage
    {
        public required string Title { get; set; }
        public required string Path { get; set; }
        public int Order { get; set; }
        public string? Layout { get; set; }
        public required List<DefaultPlugin> Plugins { get; set; }

        public Page GetPage(Guid siteId) => new()
        {
            Title = Title,
            Path = Path,
            Order = Order,
            SiteId = siteId
        };
    }

    public class DefaultPlugin
    {
        public required string DefName { get; set; }
        public required string Section { get; set; }
        public required int Order { get; set; }
    }

    public List<Page> GetPages()
    {
        var pages = new List<Page>();

        foreach (var page in Pages)
        {
            var _page = page.GetPage(_site.Id);
            if (!string.IsNullOrEmpty(page.Layout))
                _page.LayoutId = GetLayout(page.Layout).Id;
            pages.Add(_page);
        }

        return pages;
    }

    public void SetLayouts(IEnumerable<Layout> tempLayouts)
    {
        foreach (var tempLayout in tempLayouts)
        {
            var layout = GetLayout(tempLayout.Name);
            layout.Body = tempLayout.Body;
            layout.Head = tempLayout.Head;
        }
    }

    public Layout GetLayout(string name)
    {
        var layout = Layouts.SingleOrDefault(x => x.Name == name);
        if (layout is null)
            throw new Exception($"Layout {name} not found");

        layout.SiteId = _site.Id;
        return layout;
    }

    private Site _site = new() { Name = "" };
    public void SetSite(Site site)
    {
        _site = site;
    }
}
