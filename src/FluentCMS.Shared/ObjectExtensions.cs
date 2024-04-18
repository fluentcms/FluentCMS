using System.Text.Json;

namespace FluentCMS.Shared;
public static class ObjectExtensions
{
    public static string SerializeToJson<T>(this T obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    public static T? DeserializeFromJson<T>(this string json)
    {
        return JsonSerializer.Deserialize<T?>(json);
    }
}
