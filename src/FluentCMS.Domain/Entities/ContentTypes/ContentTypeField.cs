

namespace FluentCMS.Entities.ContentTypes;

public record ContentTypeField(FieldType FieldType,bool Required, string? Regex)
{
}