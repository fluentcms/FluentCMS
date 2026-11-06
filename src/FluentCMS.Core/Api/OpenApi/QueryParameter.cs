using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;

namespace FluentCMS.Core.Api.OpenApi;

// A query parameter that a route beside content reads by itself. The route accepts and reads only the parameters it
// declares, and the OpenAPI document describes them, so the two cannot drift apart.
internal sealed record QueryParameter(string Name, string Description, JsonObject Schema)
{
    // ManagementQuery refuses an empty value and a repeated key, so a text has at least one character and one value.
    public static QueryParameter Text(string name, string description) =>
        new(name, description, new JsonObject { ["type"] = "string", ["minLength"] = 1 });

    public static QueryParameter Id(string name, string description) =>
        new(name, description, new JsonObject { ["type"] = "string", ["format"] = "uuid" });

    public static QueryParameter Moment(string name, string description) =>
        new(name, description, new JsonObject { ["type"] = "string", ["format"] = "date-time" });

    public static QueryParameter OneOf(string name, IEnumerable<string> values, string description) =>
        new(name, description, SharedComponents.Enum(values));

    public static QueryParameter Enum<TEnum>(string name, string description)
        where TEnum : struct, System.Enum =>
        OneOf(name, System.Enum.GetNames<TEnum>().Select(JsonNamingPolicy.CamelCase.ConvertName), description);

    public static QueryParameter Number(string name, string description, int minimum, int? maximum, int @default)
    {
        JsonObject schema = new() { ["type"] = "integer", ["format"] = "int32", ["minimum"] = minimum };
        if (maximum is { } most)
        {
            schema["maximum"] = most;
        }

        schema["default"] = @default;
        return new(name, description, schema);
    }

    public JsonObject ToJson() => SharedComponents.Query(Name, Schema.DeepClone().AsObject(), Description);
}

internal static class QueryParameterExtensions
{
    public static RouteHandlerBuilder Query(this RouteHandlerBuilder builder, params QueryParameter[] parameters) =>
        builder.WithMetadata(parameters);
}
