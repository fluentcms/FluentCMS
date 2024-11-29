namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;
public interface IFieldValue
{
    public string Name { get; set; }
    public object? GetValue();
}
public class FieldValue<T> : IFieldValue
{
    public string Name { get; set; } = default!;
    public T Value { get; set; } = default!;

    public object? GetValue()
    {
        return Value;
    }
}
