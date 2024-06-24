using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.Plugins.Contents.News;

public partial class NewsEditPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(NewsContent);

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private NewsContent? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var x = Plugin;
        Model ??= new();
        await Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        if (Model is null || Plugin is null)
        {
            return;
        }
        await GetApiClient<PluginContentClient>().CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.AsDictionary());
        NavigateBack();
    }
}
