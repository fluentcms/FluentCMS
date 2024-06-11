namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseFieldComponent<T, TField> : ComponentBase where TField : IFieldModel<T>
{
    [Parameter]
    public TField Field { get; set; } = default!;

}

public abstract class BaseFieldValueComponent<T, TField> : BaseFieldComponent<T, TField> where TField : IFieldModel<T>
{
    [Parameter]
    public FieldValue<T> FieldValue { get; set; } = default!;
}
