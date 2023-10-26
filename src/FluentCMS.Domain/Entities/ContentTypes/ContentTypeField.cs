namespace FluentCMS.Entities.ContentTypes;


public abstract class ContentTypeField
{
    public virtual Type Type => throw new NotImplementedException();
}
public abstract class ContentTypeField<T>: ContentTypeField
{
    public ContentTypeField(FieldType<T> fieldType,T initialValue)
    {
        FieldType = fieldType;
        SetValue(initialValue);
    }
    public override Type Type => typeof(T);
    public FieldType<T> FieldType { get; }
    public T? FieldValue { get; protected set; }
    public virtual void SetValue(T value)
    {
        // validate the value
        FieldValue = value;
    }
}