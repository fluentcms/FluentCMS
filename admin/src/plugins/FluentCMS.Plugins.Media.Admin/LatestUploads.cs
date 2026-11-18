using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.Media.Admin;

// An asset as the widget of the latest uploads reads it from GET media, which lists the newest first.
internal sealed record LatestUpload(Guid Id, string FileName, string? Title);

internal sealed record LatestUploadPage(IReadOnlyList<LatestUpload> Items);

internal static class LatestUploads
{
    public const int Count = 5;

    public static async Task<IReadOnlyList<LatestUpload>> ReadAsync(IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, $"media?pageSize={Count}"), LatestUploadsJson.Default.LatestUploadPage, cancellationToken)).Value.Items;
}

// Source-generated, since the admin is published trimmed.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LatestUploadPage))]
internal sealed partial class LatestUploadsJson : JsonSerializerContext;
