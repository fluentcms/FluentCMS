﻿namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    public const string FORM_NAME = "ContentTypeFieldCreateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    [CascadingParameter]
    public List<FieldType> FieldTypes { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private FieldType? FieldType { get; set; }

    private async Task OnBack()
    {
        Model!.Type = default!;
        FieldType = default!;
        await Task.CompletedTask;
    }

    private async Task OnTypeSelect(FieldType type)
    {
        Model!.Type = type.Key;
        FieldType = type;
        await Task.CompletedTask;
    }
}
