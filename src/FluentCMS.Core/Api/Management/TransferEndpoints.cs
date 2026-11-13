using System.Globalization;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Transfer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The transfer group of spec 4.14: an export answers the package as a stream, and an import takes it as its body and
// answers a line of JSON per step and a last line with the report. Both need transfer.manage in the site of the request and
// run within the request; a caller that goes away cancels them.
internal static class TransferEndpoints
{
    public const string ZipType = "application/zip";
    public const string LinesType = "application/x-ndjson";

    private static readonly string[] ZipTypes = [ZipType, "application/x-zip-compressed", "application/octet-stream"];
    private static readonly string[] Flags = ["true", "false"];
    private static readonly string[] MissingLocales = ["refuse", "skip"];

    public static IServiceCollection AddManagementTransferApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var transfer = management.MapGroup("/transfer").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        transfer.MapPost("/export", ExportAsync)
            .Takes<TransferExportBody>(optional: true, "What to export: the content types, whether global types and roles are in, whether only published entries are, and the options of the plugins' contributors, such as mediaFiles.")
            .Produces<Stream>(StatusCodes.Status200OK, ZipType);
        transfer.MapPost("/import", ImportAsync)
            .WithMetadata(new PackageSizeLimit())
            .Query(
            [
                QueryParameter.OneOf("dryRun", Flags, "true checks everything and writes nothing, and answers the same report."),
                QueryParameter.OneOf("overwrite", Flags, "true takes the package's copy of an entry that is newer in the instance, which is otherwise a conflict."),
                QueryParameter.OneOf("missingLocales", MissingLocales, "skip leaves out the locales of the package that the site does not allow, which otherwise refuse the import."),
                QueryParameter.OneOf("webhooks", Flags, "true delivers the webhooks of what the import writes, which are otherwise not delivered."),
                QueryParameter.OneOf("roles", Flags, "true imports the roles of the package too."),
                QueryParameter.Text("types", "The content types to import, separated by commas; every type of the package when left out. A caller with transfer.manage in the site only names the types it may import: an export has a file for every global type, empty or not, and importing one needs transfer.manage for the instance."),
                QueryParameter.Text("options", "Options of the plugins' contributors, as name=value pairs separated by commas."),
            ])
            .Produces<string>(StatusCodes.Status200OK, LinesType);
    }

    private static async Task ExportAsync(HttpContext httpContext, [FromServices] TransferExporter exporter)
    {
        var request = httpContext.Request;
        ManagementQuery.Only(request);
        var asked = await ReadExportAsync(request);
        var run = await exporter.PrepareAsync(asked, httpContext.RequestAborted);

        // A zip written to a body that cannot seek ends each file with a write that is not asynchronous.
        if (httpContext.Features.Get<IHttpBodyControlFeature>() is { } control)
        {
            control.AllowSynchronousIO = true;
        }

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = ZipType;
        response.Headers.ContentDisposition = $"attachment; filename=\"fluentcms-{run.Site.Slug}-{run.Operation.Timestamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}.zip\"";
        await exporter.WriteAsync(run, response.Body, httpContext.RequestAborted);
    }

    private static async Task ImportAsync(HttpContext httpContext, [FromServices] TransferImporter importer, [FromServices] TransferOptions options)
    {
        var request = httpContext.Request;
        ManagementQuery.Only(request);
        var asked = ReadImport(request);
        if (request.ContentType is not { } contentType || !ZipTypes.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BadHttpRequestException($"The body is a package, sent as {ZipType}.", StatusCodes.Status415UnsupportedMediaType);
        }

        if (httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = options.MaxPackageSize;
        }

        await using var run = await importer.PrepareAsync(request.Body, request.ContentLength, asked, httpContext.RequestAborted);
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = LinesType;
        var result = await importer.RunAsync(run, (line, token) => WriteLineAsync(response, line, token), httpContext.RequestAborted);
        await WriteLineAsync(response, new { report = result }, httpContext.RequestAborted);
    }

    private static async Task WriteLineAsync(HttpResponse response, object line, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(response.Body, line, line.GetType(), TransferPackage.Json, cancellationToken);
        await response.Body.WriteAsync(TransferPackage.NewLine, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    // No body is every content type with the defaults.
    private static async Task<TransferExportRequest> ReadExportAsync(HttpRequest request)
    {
        if (request.ContentLength is 0 || (request.ContentLength is null && request.ContentType is null))
        {
            return new TransferExportRequest();
        }

        var body = await BodyReader.ReadAsync(request, "types", "globalTypes", "publishedOnly", "roles", "options");
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        if (body.Element("options") is { ValueKind: not JsonValueKind.Null } element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.EnumerateObject().All(property => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number))
            {
                foreach (var property in element.EnumerateObject())
                {
                    options[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                }
            }
            else
            {
                body.Error("options", "'options' is an object of names and values.");
            }
        }

        var asked = new TransferExportRequest
        {
            Types = body.Strings("types"),
            GlobalTypes = body.Boolean("globalTypes") ?? true,
            PublishedOnly = body.Boolean("publishedOnly") ?? false,
            Roles = body.Boolean("roles") ?? false,
            Options = options,
        };
        body.ThrowIfInvalid();
        return asked;
    }

    private static TransferImportRequest ReadImport(HttpRequest request)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        if (ManagementQuery.Text(request, "options") is { } text)
        {
            foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var split = pair.IndexOf('=', StringComparison.Ordinal);
                if (split <= 0)
                {
                    throw Invalid("options", $"'{pair}' is not a name=value pair.");
                }

                options[pair[..split]] = pair[(split + 1)..];
            }
        }

        return new TransferImportRequest
        {
            DryRun = Flag(request, "dryRun"),
            Overwrite = Flag(request, "overwrite"),
            SkipMissingLocales = ManagementQuery.Text(request, "missingLocales") is { } missing && Choice(missing, "missingLocales", MissingLocales) == "skip",
            Webhooks = Flag(request, "webhooks"),
            Roles = Flag(request, "roles"),
            Types = ManagementQuery.Text(request, "types")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Options = options,
        };
    }

    private static bool Flag(HttpRequest request, string name) =>
        ManagementQuery.Text(request, name) is { } text && Choice(text, name, Flags) == "true";

    private static string Choice(string text, string name, string[] allowed) =>
        allowed.Contains(text, StringComparer.Ordinal) ? text : throw Invalid(name, $"'{text}' is not one of {string.Join(", ", allowed)}.");

    private static ValidationException Invalid(string name, string message) => new([new ValidationError(name, QueryErrorCodes.InvalidParameter, message)]);

    // The import reads its body up to FluentCms:Transfer:MaxPackageSize, which it sets itself, so the cap of the other
    // requests does not apply.
    private sealed class PackageSizeLimit : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => null;
    }
}

// The body of an export, as the OpenAPI document describes it.
internal sealed record TransferExportBody(IReadOnlyList<string>? Types, bool? GlobalTypes, bool? PublishedOnly, bool? Roles, IReadOnlyDictionary<string, string>? Options);
