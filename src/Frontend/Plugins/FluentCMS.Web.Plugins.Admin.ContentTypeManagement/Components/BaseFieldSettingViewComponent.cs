namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseFieldSettingViewComponent
{
    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ContentTypeField> OnSubmit { get; set; }
}
