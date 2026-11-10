using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Content;
using FluentCMS.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Plugins.Media;

internal sealed record AssetMove(Guid? FolderId);

internal sealed record FolderCreate(string? Name, Guid? ParentId);

internal sealed record FolderRename(string? Name);

internal sealed record FolderMove(Guid? ParentId);

// /api/management/v1/media. The service checks the media permissions for the site of the request; an upload or a replace
// reads the multipart body here, bounded by FluentCms:Media:MaxRequestBodySize, and hands its files to the service. Presets
// are read and written with enum values as camel case names, as the parameters of a URL name them.
internal static class MediaEndpoints
{
    public const string RootFolder = "root";

    // An SVG is a document that can carry script, so a browser that opens one gets nothing to run and nowhere to send.
    public const string SvgPolicy = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; sandbox";

    private static readonly JsonSerializerOptions PresetJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static RouteGroupBuilder MapMediaEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (string? folder, bool? subfolders, string? type, string? tag, string? search, int? page, int? pageSize, IMediaService media, CancellationToken cancellationToken) =>
        {
            var query = new MediaQuery
            {
                FolderId = folder is null || folder == RootFolder ? null : Guid.TryParse(folder, out var id) ? id : throw Invalid("folder", $"A folder is an id or '{RootFolder}'."),
                RootOnly = folder == RootFolder,
                IncludeSubfolders = subfolders ?? false,
                MimeType = type,
                Tag = tag,
                Search = search,
                Page = page ?? 1,
                PageSize = pageSize ?? 50,
            };
            var result = await media.ListAsync(query, cancellationToken);
            return Results.Ok(new { items = result.Items, total = result.Total, page = result.Page, pageSize = result.PageSize });
        });

        group.MapPost("", UploadAsync);

        group.MapGet("/{id:guid}", async (Guid id, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.GetAsync(id, cancellationToken)));

        group.MapPatch("/{id:guid}", async (Guid id, MediaAssetChanges changes, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.UpdateAsync(id, changes, cancellationToken)));

        group.MapPost("/{id:guid}/move", async (Guid id, AssetMove move, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.MoveAsync(id, move.FolderId, cancellationToken)));

        group.MapDelete("/{id:guid}", async (Guid id, bool? force, IMediaService media, CancellationToken cancellationToken) =>
        {
            await media.DeleteAsync(id, force ?? false, cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/replace", ReplaceAsync);

        group.MapGet("/{id:guid}/usage", async (Guid id, IMediaService media, CancellationToken cancellationToken) =>
        {
            var usage = await media.GetUsageAsync(id, cancellationToken);
            return Results.Ok(new
            {
                items = usage.Select(reference => new
                {
                    type = reference.Type,
                    entryId = reference.EntryId,
                    status = reference.Status == QueryStatus.Published ? "published" : "draft",
                    locale = reference.Locale,
                    field = reference.Field,
                }),
            });
        });

        group.MapPost("/{id:guid}/download-url", async (Guid id, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.GetDownloadUrlAsync(id, cancellationToken)));

        group.MapGet("/folders", async (IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(new { items = await media.ListFoldersAsync(cancellationToken) }));

        group.MapPost("/folders", async (FolderCreate request, IMediaService media, CancellationToken cancellationToken) =>
        {
            var created = await media.CreateFolderAsync(request.Name ?? "", request.ParentId, cancellationToken);
            return Results.Created($"media/folders/{created.Id:D}", created);
        });

        group.MapPatch("/folders/{id:guid}", async (Guid id, FolderRename request, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.RenameFolderAsync(id, request.Name ?? "", cancellationToken)));

        group.MapPost("/folders/{id:guid}/move", async (Guid id, FolderMove request, IMediaService media, CancellationToken cancellationToken) =>
            Results.Ok(await media.MoveFolderAsync(id, request.ParentId, cancellationToken)));

        group.MapDelete("/folders/{id:guid}", async (Guid id, IMediaService media, CancellationToken cancellationToken) =>
        {
            await media.DeleteFolderAsync(id, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/presets", async (IMediaService media, CancellationToken cancellationToken) =>
            Results.Json(new { items = await media.ListPresetsAsync(cancellationToken) }, PresetJson));

        group.MapPut("/presets/{name}", async (string name, HttpRequest request, IMediaService media, CancellationToken cancellationToken) =>
        {
            MediaImageOptions? options;
            try
            {
                options = await JsonSerializer.DeserializeAsync<MediaImageOptions>(request.Body, PresetJson, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw Invalid(exception.Path is { Length: > 2 } path ? path[2..] : "", "The body is a JSON object of the parameters of the preset.");
            }

            return Results.Json(await media.SetPresetAsync(name, options ?? throw Invalid("", "The body is a JSON object of the parameters of the preset."), cancellationToken), PresetJson);
        });

        group.MapDelete("/presets/{name}", async (string name, IMediaService media, CancellationToken cancellationToken) =>
        {
            await media.DeletePresetAsync(name, cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    // /api/delivery/v1/media/{id}, under the Delivery API's authentication, rate limit, CORS and output cache.
    public static RouteGroupBuilder MapDeliveryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, IMediaService media, IOperationContextAccessor operations, CancellationToken cancellationToken) =>
        {
            var delivered = await media.DeliverAsync(id, cancellationToken);
            var operation = operations.Current ?? throw new InvalidOperationException("A Delivery read runs inside an operation.");
            return new MediaDeliveryResponse(delivered, operation.Site.Value, shared: operation.Principal.IsAnonymous && !delivered.Asset.IsPrivate);
        });

        return group;
    }

    // /media/{id}/{fileName} and /media/{id}/original/{fileName}, outside the APIs and open to anyone: the signature of the
    // URL is the authority, and the file name is there for the reader and is ignored. The transform says whether the type
    // depends on Accept. An original is sent as it is stored, with nosniff and, for an SVG, a policy that runs nothing; a
    // download URL's answer is an attachment that no cache keeps.
    public static RouteGroupBuilder MapImageEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/original/{fileName}", async (Guid id, HttpContext context, MediaOriginals originals, IOptions<MediaOptions> options, CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query.SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
            var file = await originals.OpenAsync(id, query, cancellationToken);

            var headers = context.Response.Headers;
            headers.CacheControl = file.Download ? "private, no-store" : options.Value.CacheControl;
            headers.XContentTypeOptions = "nosniff";
            if (file.MimeType == MediaTypes.Svg.MimeType)
            {
                headers.ContentSecurityPolicy = SvgPolicy;
            }

            return Results.Stream(file.Content, file.MimeType, file.Download ? file.FileName : null, entityTag: new EntityTagHeaderValue($"\"{file.Checksum}\""));
        }).AllowAnonymous();

        group.MapGet("/{id:guid}/{fileName}", async (Guid id, HttpContext context, MediaTransforms transforms, IOptions<MediaOptions> options, CancellationToken cancellationToken) =>
        {
            var request = context.Request;
            var query = request.Query.SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
            var accepted = request.GetTypedHeaders().Accept
                .Where(type => type.Quality is not 0)
                .Select(type => type.MediaType.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var image = await transforms.TransformAsync(id, query, accepted, cancellationToken);

            var headers = context.Response.Headers;
            headers.CacheControl = options.Value.CacheControl;
            if (image.VariesByAccept)
            {
                headers.Append(HeaderNames.Vary, HeaderNames.Accept);
            }

            return Results.Stream(image.Content, image.MimeType, entityTag: new EntityTagHeaderValue(image.ETag));
        }).AllowAnonymous();

        return group;
    }

    // Every file part of the form is a file of the upload, whatever its field name; the field folderId names the folder.
    private static async Task<IResult> UploadAsync(HttpRequest request, IMediaService media, IOptions<MediaOptions> options, CancellationToken cancellationToken)
    {
        var form = await ReadFormAsync(request, options.Value, "An upload is a multipart/form-data request with one or more files.", cancellationToken);
        Guid? folderId = form["folderId"].ToString() is { Length: > 0 } folder
            ? Guid.TryParse(folder, out var id) ? id : throw Invalid("folderId", "The folder is an id.")
            : null;
        List<Stream> streams = [];
        try
        {
            List<MediaUploadFile> files = [];
            foreach (var file in form.Files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                files.Add(new MediaUploadFile(file.FileName, stream));
            }

            var created = await media.UploadAsync(new MediaUpload(folderId, files), cancellationToken);
            return Results.Created((string?)null, new { items = created });
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    // The form's one file part, whatever its field name, is the new file.
    private static async Task<IResult> ReplaceAsync(Guid id, HttpRequest request, IMediaService media, IOptions<MediaOptions> options, CancellationToken cancellationToken)
    {
        const string Shape = "A replace is a multipart/form-data request with one file.";
        var form = await ReadFormAsync(request, options.Value, Shape, cancellationToken);
        if (form.Files.Count != 1)
        {
            throw Invalid("files", Shape);
        }

        var file = form.Files[0];
        await using var stream = file.OpenReadStream();
        return Results.Ok(await media.ReplaceAsync(id, new MediaUploadFile(file.FileName, stream), cancellationToken));
    }

    private static async Task<IFormCollection> ReadFormAsync(HttpRequest request, MediaOptions options, string shape, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw Invalid("files", shape);
        }

        var limit = options.MaxRequestBodySize;
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            body.MaxRequestBodySize = limit;
        }

        try
        {
            return await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = limit, BufferBodyLengthLimit = limit }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException || (exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }))
        {
            throw new PluginException(MediaErrorCodes.TooLarge, $"The request is over the limit of {limit} bytes a request.");
        }
    }

    private static ValidationException Invalid(string path, string message) => MediaService.Invalid(path, message);
}
