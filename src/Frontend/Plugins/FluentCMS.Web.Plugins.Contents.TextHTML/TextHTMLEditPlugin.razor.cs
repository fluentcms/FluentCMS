using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.Plugins.Contents.TextHTML;

public partial class TextHTMLEditPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(TextHTMLContent);

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private TextHTMLContent? Model { get; set; }

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    private Guid? Id { get; set; } = Guid.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin!.Id);

            if (response?.Data != null && response.Data.ToContentList<TextHTMLContent>().Any()) 
            {
                var content = response.Data.ToContentList<TextHTMLContent>()[0];
                Id = content.Id;
                
                Model = new TextHTMLContent {
                    Id = Plugin!.Id,
                    Content = content.Content;
                };
            }
            else
            {
                Model = new TextHTMLContent {
                    Id = Plugin!.Id,
                    Content = String.Empty;
                };
            }
        }
    }

    private async Task OnSubmit()
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
