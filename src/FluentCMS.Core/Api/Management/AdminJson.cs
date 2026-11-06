using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Management;

// The shapes of the groups beside content: the services' records with camel case names and enum values, and a list as
// items with its paging, as content lists are.
internal static class AdminJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IResult Ok(object value, int statusCode = StatusCodes.Status200OK) => Results.Json(value, Options, statusCode: statusCode);

    public static IResult Items<T>(IEnumerable<T> items) => Results.Json(new ItemList<T>(items), Options);

    public static IResult Page<T>(IEnumerable<T> items, long total, int page, int pageSize) => Results.Json(new ItemPage<T>(items, total, page, pageSize), Options);
}
