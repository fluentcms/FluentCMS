using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Settings;
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

    public static IResult Items<T>(IEnumerable<T> items) => Results.Json(new { items }, Options);

    public static IResult Page<T>(IEnumerable<T> items, long total, int page, int pageSize) => Results.Json(new { items, total, page, pageSize }, Options);

    public static JsonObject Role(RoleInfo role) => new()
    {
        ["id"] = role.Id,
        ["siteId"] = role.SiteId,
        ["name"] = role.Name,
        ["description"] = role.Description,
        ["builtIn"] = role.BuiltIn,
        ["grants"] = new JsonArray([.. role.Grants.Select(grant => (JsonNode)new JsonObject
        {
            ["permission"] = grant.Permission,
            ["constraints"] = grant.Constraints.ToJson() is { } json ? JsonNode.Parse(json) : null,
        })]),
    };

    // The secret is in this response only.
    public static JsonObject CreatedKey(CreatedApiKey created)
    {
        var json = (JsonObject)JsonSerializer.SerializeToNode(created.Key, Options)!;
        json["secret"] = created.Secret;
        return json;
    }

    public static JsonObject Setting(SettingsDescriptor descriptor, Guid? site, RedactedSetting setting) => new()
    {
        ["area"] = descriptor.Area,
        ["siteId"] = site,
        ["source"] = JsonNamingPolicy.CamelCase.ConvertName(setting.Source.ToString()),
        ["version"] = setting.Version,
        ["value"] = JsonNode.Parse(setting.Json),
        ["secretsSet"] = new JsonArray([.. setting.SecretsSet.Select(key => (JsonNode)key)]),
        ["schema"] = descriptor.CreateSchema(),
    };
}
