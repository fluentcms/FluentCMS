﻿namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeUpdatePlugin
{
    public const string FORM_NAME = "ContentTypeUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ContentTypeUpdateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var contentTypeResponse = await GetApiClient<ContentTypeClient>().GetByIdAsync(Id);
            Model = Mapper.Map<ContentTypeUpdateRequest>(contentTypeResponse.Data);
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<ContentTypeClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
