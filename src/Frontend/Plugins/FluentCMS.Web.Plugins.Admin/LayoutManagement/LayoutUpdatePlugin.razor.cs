namespace FluentCMS.Web.Plugins.Admin.LayoutManagement;

public partial class LayoutUpdatePlugin
{
    public const string FORM_NAME = "LayoutUpdateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private LayoutUpdateRequest? Model { get; set; }

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var layoutResponse = await ApiClient.Layout.GetAsync(Id);
            var layout = layoutResponse.Data;
            Model = Mapper.Map<LayoutUpdateRequest>(layout);
        }
    }

    private async Task OnSubmit()
    {
        Model!.Id = Id;
        await ApiClient.Layout.UpdateAsync(Model);
        NavigateBack();
    }
}
