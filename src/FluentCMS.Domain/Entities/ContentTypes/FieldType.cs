namespace FluentCMS.Entities.ContentTypes;

public record FieldType(string TypeId, Type Type)
{
    public readonly static FieldType Text = new("text", typeof(string));
    public readonly static FieldType Number = new("number", typeof(decimal));
    public readonly static FieldType Date = new("date", typeof(DateOnly));
    public readonly static FieldType DateTime = new("date-time", typeof(DateTime));
    public readonly static FieldType Bool = new("bool", typeof(bool));

    public static FieldType FindById(string id)
    {
        return id switch
        {
            "text" => Text,
            "number" => Number,
            "date" => Date,
            "date-time" => DateTime,
            "bool" => Bool,
            _ => throw new IndexOutOfRangeException()
        };
    }
}