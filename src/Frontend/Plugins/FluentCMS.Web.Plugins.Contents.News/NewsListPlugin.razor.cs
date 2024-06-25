using FluentCMS.Web.ApiClients;

namespace FluentCMS.Web.Plugins.Contents.News;
public partial class NewsListPlugin
{
    private List<NewsContent>? Contents { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Plugin is not null)
        {
            var response = await GetApiClient<PluginContentClient>().GetAllAsync(nameof(NewsContent), Plugin.Id);
            Contents = response?.Data?.ToContentList<NewsContent>() ?? [];
        }
    }
}
