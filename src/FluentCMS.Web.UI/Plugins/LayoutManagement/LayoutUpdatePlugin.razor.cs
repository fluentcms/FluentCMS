namespace FluentCMS.Web.UI.Plugins;
public partial class LayoutUpdatePlugin
{
    public const string FORM_NAME = "LayoutUpdateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private LayoutUpdateRequest Model { get; set; } = new();

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var layoutResponse = await GetApiClient<LayoutClient>().GetAsync(Id);
        var layout = layoutResponse.Data;
        Model.IsDefault = layout.IsDefault;
        Model.Name = layout.Name;
        Model.Head = layout.Head;
        Model.Body = layout.Body;
    }

    private async Task OnSubmit()
    {
        await GetApiClient<LayoutClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
