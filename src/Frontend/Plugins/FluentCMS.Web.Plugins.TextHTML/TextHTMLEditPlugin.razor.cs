namespace FluentCMS.Web.Plugins.TextHTML;
using FluentCMS.Providers.MessageBusProviders;
using FluentCMS.Web.Plugins.Base;

public partial class TextHTMLEditPlugin
{
    [Inject]
    private IMessagePublisher MessagePublisher { get; set; } = default!;

    public const string CONTENT_TYPE_NAME = nameof(TextHTMLContent);

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private TextHTMLContent? Model { get; set; }

    private bool IsEditMode { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin!.Id);

            var content = response.Data?.ToContentList<TextHTMLContent>() ?? [];

            if (content.Count > 0)
            {
                Model = new TextHTMLContent
                {
                    Id = content[0].Id,
                    Content = content[0].Content,
                };
                IsEditMode = true;
            }
            else
            {
                Model = new TextHTMLContent
                {
                    Id = Guid.Empty
                };
            }
        }
    }

    private async Task HandleSubmit()
    {
        if (Model is null || Plugin is null)
            return;

        if (IsEditMode)
            await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.Id, Model.ToDictionary());
        else
            await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());

        await MessagePublisher.Publish(new Message<string>(ActionNames.InvalidateStyles, Path.Combine(ViewState.Site.Id.ToString(), ViewState.Page.Id.ToString() + ".css")));
        await OnSubmit.InvokeAsync();
    }
}
