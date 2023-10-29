

using System.Dynamic;

namespace FluentCMS.Entities.ContentTypes;

public class ContentTypeField
{
    public ContentTypeField(FieldType fieldType, IFieldOptions options)
    {
        if(fieldType != options.FieldType)
        {
            throw new Exception("Field type does not match options type");
        }
        FieldType = fieldType;
        Options = options;
    }


    public FieldType FieldType { get; }
    public IFieldOptions Options { get; }
}