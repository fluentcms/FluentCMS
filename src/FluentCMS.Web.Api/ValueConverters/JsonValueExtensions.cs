using System.Text.Json;

namespace FluentCMS.Web.Api.ValueConverters;

public static class JsonValueExtensions
{
    public static object? MapValue(this object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is not JsonElement)
        {
            return value;
        }

        var jsonElement = (JsonElement)value;
        return jsonElement.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.Null => null,
            JsonValueKind.Object => jsonElement.EnumerateObject()
                .Select(x => (x.Name, Value: MapValue(x.Value)))
                .ToDictionary(x => x.Name, x => x.Value),
            JsonValueKind.Array => jsonElement.EnumerateArray().Select(x => MapValue(x)).ToArray(),
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement
                .GetDecimal(), // todo: find a better way for this as most of our values don't need to be Decimal
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }
}
