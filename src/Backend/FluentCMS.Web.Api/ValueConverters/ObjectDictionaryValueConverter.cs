using System.Text.Json;

namespace FluentCMS.Web.Api.ValueConverters;

public class ObjectDictionaryValueConverter :
    IValueConverter<Dictionary<string, object?>, Dictionary<string, object?>>
{
    public Dictionary<string, object?> Convert(Dictionary<string, object?> sourceMember, ResolutionContext context)
    {
        return sourceMember.Select(x => (x.Key, Value: mapValue(x.Value)))
            .ToDictionary(x => x.Key, x => x.Value);
    }

    private object? mapValue(object value)
    {
        var jsonElement = (JsonElement)value;
        return jsonElement.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.Null => null,
            JsonValueKind.Object => jsonElement.EnumerateObject()
                .Select(x => (x.Name, Value: mapValue(x.Value)))
                .ToDictionary(x => x.Name, x => x.Value),
            JsonValueKind.Array => jsonElement.EnumerateArray().Select(x => mapValue(x)).ToArray(),
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement
                .GetDecimal(), // todo: find a better way for this as most of our values don't need to be Decimal
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }
}
