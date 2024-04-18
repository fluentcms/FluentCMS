using System.Text.Json;

namespace FluentCMS.Shared;
public static class ObjectExtensions
{
    public static string SerializeToJson<T>(this T obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    public static T? DeserializeToJson<T>(this string json)
    {
        return JsonSerializer.Deserialize<T?>(json);
    }
}
