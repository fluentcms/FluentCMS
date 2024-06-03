namespace FluentCMS.Admin.LayoutManagement;

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
            Model = Mapper.Map<LayoutUpdateRequest>(layout);
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<LayoutClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
