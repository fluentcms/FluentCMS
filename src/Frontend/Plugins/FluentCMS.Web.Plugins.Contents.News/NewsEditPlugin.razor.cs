using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.Plugins.Contents.News;

public partial class NewsEditPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(NewsContent);

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private NewsContent? Model { get; set; }

    private bool IsEditMode() => Id != Guid.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            if (IsEditMode())
            {
                var response = await GetApiClient<PluginContentClient>().GetByIdAsync(CONTENT_TYPE_NAME, Plugin!.Id, Id);
                var pluginContentResponse = response.Data;
                if (pluginContentResponse != null)
                    Model = pluginContentResponse.Data!.ToContent<NewsContent>();
            }
            else
            {
                Model = new NewsContent();
            }
        }
    }

    private async Task Submit()
    {
        if (Model is null || Plugin is null)
            return;

        if (IsEditMode())
            await GetApiClient<PluginContentClient>().UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Id, Model.ToDictionary());
        else
            await GetApiClient<PluginContentClient>().CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());

        NavigateBack();
    }
}
