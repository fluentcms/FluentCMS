namespace FluentCMS.Web.UI.Plugins;

public partial class LayoutUpdatePlugin
{
    public const string FORM_NAME = "LayoutUpdateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private LayoutUpdateRequest? Model { get; set; }

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Model is null)
        {
            var layoutResponse = await GetApiClient<LayoutClient>().GetAsync(Id);
            var layout = layoutResponse.Data;
            Model = new LayoutUpdateRequest
            {
                Id = Id,
                IsDefault = layout.IsDefault,
                Name = layout.Name,
                Head = layout.Head,
                Body = layout.Body
            };
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<LayoutClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
