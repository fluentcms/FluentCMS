namespace FluentCMS.Entities.ContentTypes;

public record ContentTypeField(string TypeId, Type Type)
{
    public readonly static ContentTypeField Text = new("text", typeof(string));
    public readonly static ContentTypeField Number = new("number", typeof(decimal));
    public readonly static ContentTypeField Date = new("date", typeof(DateOnly));
    public readonly static ContentTypeField DateTime = new("date-time", typeof(DateTime));
    public readonly static ContentTypeField Bool = new("bool", typeof(bool));

    public static ContentTypeField FindById(string id)
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