using FluentCMS.Providers.TemplateRenderingProviders.Abstractions;

namespace FluentCMS.Web.Plugins.ContentViewer;

public partial class ContentDetailViewPlugin
{
    [Inject]
    protected ITemplateRenderingProvider RenderingProvider { get; set; } = default!;

    private ContentDetailResponse Item { get; set; } = default!;
    private string Rendered { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            if (Plugin.Settings.TryGetValue("ContentTypeSlug", out var slug) && !string.IsNullOrEmpty(slug))
            {
                if (Guid.TryParse(ViewState.Page.Slug, out var id))
                {
                    var response = await ApiClient.Content.GetByIdAsync(ViewState.Site.Id, slug, id);
                    if (response.Data != null)
                    {
                        Item = response.Data;
                    }
                }
                else
                {
                    var response = await ApiClient.Content.GetAllAsync(ViewState.Site.Id, slug);
                    if (response.Data != null)
                    {
                        Item = response.Data.LastOrDefault() ?? default!;
                    }
                }
            }

            if (Item != null)
            {
                var data = new Dictionary<string, object>
                {
                    { "Item", Item.Data }
                };
                var content = Plugin.Settings["Template"] ?? "No Template";
                Rendered = RenderingProvider.Render(content, data);
            }
        }
    }
}
