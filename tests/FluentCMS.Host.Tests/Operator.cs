using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

// The operator of an instance: a confidential client set up by environment variables, holding Super Admin, which is
// how a new instance gets its first invitation and its keys.
internal static class Operator
{
    public const string ClientId = "ops";
    public const string Secret = "an-operator-secret-from-the-secret-store";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    public static Dictionary<string, string?> Settings() => new()
    {
        [$"FluentCms:Clients:Applications:{ClientId}:Secret"] = Secret,
        [$"FluentCms:Clients:Applications:{ClientId}:Scopes:0"] = "management",
        [$"FluentCms:Clients:Applications:{ClientId}:Roles:0"] = "Super Admin",
    };

    public static Task<HttpClient> SignInAsync(HostUnderTest host) => BearerAsync(host, new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = ClientId,
        ["client_secret"] = Secret,
        ["scope"] = "management",
    });

    public static async Task<HttpClient> BearerAsync(HostUnderTest host, Dictionary<string, string> form)
    {
        using var client = host.Client();
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(new Uri("/connect/token", UriKind.Relative), content, Token);
        var (status, body) = await HostUnderTest.ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK, body?.ToJsonString());

        var api = host.Client();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["access_token"]!.GetValue<string>());
        return api;
    }

    public static async Task<Guid> RoleIdAsync(HttpClient api, string name)
    {
        using var response = await api.GetAsync(new Uri("/api/management/v1/roles", UriKind.Relative), Token);
        var (status, body) = await HostUnderTest.ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK, body?.ToJsonString());
        return body!["items"]!.AsArray().Single(role => role!["name"]!.GetValue<string>() == name && role["siteId"] is null)!["id"]!.GetValue<Guid>();
    }

    // A role that reads the types named, and a Delivery key that holds it, as a website would use.
    public static async Task<HttpClient> DeliveryClientAsync(HostUnderTest host, HttpClient api, params string[] types)
    {
        var grants = new JsonArray([.. types.Select(type => (JsonNode)new JsonObject { ["permission"] = $"content.{type}.read" })]);
        Guid role;
        using (var created = await PostJsonAsync(api, "/api/management/v1/roles", new JsonObject { ["name"] = $"Reader {Guid.NewGuid():N}", ["grants"] = grants }))
        {
            var (status, body) = await HostUnderTest.ReadAsync(created);
            status.ShouldBe(HttpStatusCode.Created, body?.ToJsonString());
            role = body!["id"]!.GetValue<Guid>();
        }

        using var key = await PostJsonAsync(api, "/api/management/v1/api-keys", new JsonObject { ["name"] = "website", ["class"] = "delivery", ["roleId"] = role });
        var (keyStatus, keyBody) = await HostUnderTest.ReadAsync(key);
        keyStatus.ShouldBe(HttpStatusCode.Created, keyBody?.ToJsonString());

        var delivery = host.Client();
        delivery.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", keyBody!["secret"]!.GetValue<string>());
        return delivery;
    }

    // The startup report, as GET /system/diagnostics serves it.
    public static async Task<JsonNode> ReportAsync(HostUnderTest host)
    {
        using var api = await SignInAsync(host);
        using var response = await api.GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        var (status, body) = await HostUnderTest.ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK, body?.ToJsonString());
        return body!;
    }

    public static List<string> InsecureDefaults(JsonNode report) =>
        [.. report["insecureDefaults"]!.AsArray().Select(item => item!["id"]!.GetValue<string>())];

    public static async Task<HttpResponseMessage> PostJsonAsync(HttpClient api, string path, JsonObject body)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await api.PostAsync(new Uri(path, UriKind.Relative), content, Token);
    }
}
