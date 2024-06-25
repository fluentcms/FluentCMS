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
        if (Model == null)
        {
            if (IsEditMode())
            {
                var response = await GetApiClient<PluginContentClient>().GetByIdAsync(CONTENT_TYPE_NAME, Plugin!.Id, Id);
                var pluginContentResponse = response.Data;
                if (pluginContentResponse != null)
                {
                    Model = pluginContentResponse.Data!.ToContent<NewsContent>();
                    Model.Id = pluginContentResponse.Id;
                }                    
            }
            else
            {
                Model = new NewsContent();
            }
        }
    }

    private async Task OnSubmit()
    {
        if (Model is null || Plugin is null)
            return;
                
        if (IsEditMode())
            await GetApiClient<PluginContentClient>().UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.Id, Model.AsDictionary(false));
        else
            await GetApiClient<PluginContentClient>().CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.AsDictionary(true));

        NavigateBack();
    }
}
