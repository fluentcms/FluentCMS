namespace FluentCMS.Entities.ContentTypes;

public record ContentTypeField(string typeId, Type Type)
{
    public static ContentTypeField Text = new("text", typeof(string));
    public static ContentTypeField Number = new("number", typeof(decimal));
    public static ContentTypeField Date = new("date", typeof(DateOnly));
    public static ContentTypeField DateTime = new("date-time", typeof(DateTime));
    public static ContentTypeField Bool = new("bool", typeof(bool));

    public ContentTypeField FindById(string id)
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