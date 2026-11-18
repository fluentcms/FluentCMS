using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCms_Plugin1.Admin;

public sealed record StarterNote(Guid Id, string Text, DateTimeOffset CreatedAt);

public sealed record StarterSettingsValue(int MaxNotes);

// The settings as the Management API reads them, and the version a save names as If-Match.
public sealed record StarterSettingsRead(StarterSettingsValue Value, string? Version);

// The half's records are its own, read from the server half's endpoints through the admin's client, which is already set to
// the site and the locale of the admin.
public sealed class StarterApi(IManagementApi api)
{
    private const string Notes = StarterAdminPlugin.Name + "/notes";
    private const string Settings = "settings/" + StarterAdminPlugin.Name;

    // What the server said went wrong, for a component to show in place of its content.
    public static string? Describe(ApiProblemException problem) => problem.Problem.Detail ?? problem.Problem.Title;

    public async Task<IReadOnlyList<StarterNote>> NotesAsync(CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, Notes), StarterAdminJson.Default.ListStarterNote, cancellationToken)).Value;

    public async Task<StarterSettingsRead> SettingsAsync(CancellationToken cancellationToken)
    {
        var response = await api.SendAsync(new ApiRequest(HttpMethod.Get, Settings), StarterAdminJson.Default.StarterSetting, cancellationToken);
        return new(response.Value.Value, response.EntityTag);
    }

    public async Task<StarterSettingsRead> SaveSettingsAsync(StarterSettingsValue value, string? version, CancellationToken cancellationToken)
    {
        var request = new ApiRequest(HttpMethod.Put, Settings)
        {
            Body = JsonSerializer.SerializeToNode(value, StarterAdminJson.Default.StarterSettingsValue),
            IfMatch = version,
        };
        var response = await api.SendAsync(request, StarterAdminJson.Default.StarterSetting, cancellationToken);
        return new(response.Value.Value, response.EntityTag);
    }
}

public sealed record StarterSetting(StarterSettingsValue Value);

// Source-generated, since the admin is published trimmed.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<StarterNote>))]
[JsonSerializable(typeof(StarterSetting))]
internal sealed partial class StarterAdminJson : JsonSerializerContext;
