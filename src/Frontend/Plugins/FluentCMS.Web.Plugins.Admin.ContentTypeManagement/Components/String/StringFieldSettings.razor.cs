﻿namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class StringFieldSettings
{

    [Parameter, EditorRequired]
    public StringFieldModel Model { get; set; } = default!;

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ContentTypeField> OnSubmit { get; set; }

    private async Task OnFieldCreate()
    {
        await OnSubmit.InvokeAsync(Model?.ToContentTypeField());
    }
}
