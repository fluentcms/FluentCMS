namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private FieldManagementState CurrentState { get; set; } = FieldManagementState.List;
    private ContentTypeDetailResponse? ContentType { get; set; }
    private ContentTypeField? ContentTypeField { get; set; }
    private FieldTypes FieldTypes { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await LoadList();
    }

    private async Task LoadList()
    {
        CurrentState = FieldManagementState.List;
        ContentTypeField = default;
        var contentTypesResponse = await GetApiClient<ContentTypeClient>().GetByIdAsync(Id);
        ContentType = contentTypesResponse?.Data;
    }

    private async Task OnCancel()
    {
        await LoadList();
    }

    private async Task OnEditFieldClick(ContentTypeField contentTypeField)
    {
        ContentTypeField = contentTypeField;
        CurrentState = FieldManagementState.Edit;
        await Task.CompletedTask;
    }


    private async Task OnCreateFieldClick()
    {
        ContentTypeField = new ContentTypeField();
        CurrentState = FieldManagementState.Create;
        await Task.CompletedTask;
    }

    private async Task OnDeleteFieldClick(ContentTypeField contentTypeField)
    {
        ContentTypeField = contentTypeField;
        CurrentState = FieldManagementState.Delete;
        await Task.CompletedTask;
    }

    #region View Fucntions

    private static bool? IsRequired(ContentTypeField field)
    {
        if (field?.Settings == null || !field.Settings.TryGetValue("Required", out object? value))
            return default;

        if (value is bool required)
            return required;

        return default;
    }

    private static string GetString(ContentTypeField field, string key)
    {
        if (field?.Settings == null || !field.Settings.TryGetValue(key, out object? value))
            return string.Empty;

        if (value is string str)
            return str;

        return string.Empty;
    }

    private static string GetLabel(ContentTypeField field) =>
        GetString(field, "Label");

    private static string GetDescription(ContentTypeField field) =>
        GetString(field, "Description");

    #endregion
}

public enum FieldManagementState
{
    List,
    Create,
    Edit,
    Delete
}
