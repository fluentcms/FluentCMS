using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI;
public partial class PageEditorSidebar
{
    [Inject]
    protected UserLoginResponse? UserLogin { get; set; }

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    private ICollection<PluginDefinitionDetailResponse>? PluginDefinitions { get; set; } = default!;

    protected TClient GetApiClient<TClient>() where TClient : class, IApiClient
    {
        return HttpClientFactory.CreateApiClient<TClient>(UserLogin);
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var response = await GetApiClient<PluginDefinitionClient>().GetAllAsync();
            PluginDefinitions = response.Data;
        }
        catch(Exception e)
        {
            PluginDefinitions = [];
        }
    }
}
