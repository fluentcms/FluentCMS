namespace FluentCMS.Entities.ContentTypes;

public record FieldType<T>(string Id)
{
    // enum class
    public static readonly FieldType<string> Text = new("text");
    public static readonly FieldType<string> LongText = new("long-text");
    public static readonly FieldType<decimal> Number = new("number");
}