namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseFieldComponent<T, TField> : ComponentBase where TField : IFieldModel<T>
{
    [CascadingParameter]
    public TField Field { get; set; } = default!;

}

public abstract class BaseFieldValueComponent<T, TField> : BaseFieldComponent<T, TField> where TField : IFieldModel<T>
{
    [Parameter]
    public T Value { get; set; } = default!;
}
