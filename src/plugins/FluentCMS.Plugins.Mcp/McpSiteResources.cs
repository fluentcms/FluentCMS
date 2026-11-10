using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Agents;
using FluentCMS.Schema;

namespace FluentCMS.Plugins.Mcp;

// The resources of the server itself: the schema as the caller may read it, the guidelines of the site and the
// instructions of its content types. The schema comes from ISchemaIntrospection, which leaves out what the caller may not
// read, and the instructions of a type are served to a caller that reads that type there, so that no name of another
// type shows.
internal sealed class McpSiteResources(ISchemaIntrospection schema, McpInstructions instructions, IProblemDescriber problems) : IMcpResourceProvider
{
    public const string Schema = "fluentcms://schema";
    public const string SchemaTypeTemplate = "fluentcms://schema/{type}";
    public const string InstructionsTemplate = "fluentcms://types/{type}/instructions";

    private const string SchemaTypePrefix = "fluentcms://schema/";
    private const string SitePrefix = "fluentcms://sites/";
    private const string GuidelinesSuffix = "/guidelines";
    private const string TypePrefix = "fluentcms://types/";
    private const string InstructionsSuffix = "/instructions";
    private const string Json = "application/json";

    // The document of the Management API's /schema, which a client reads back into the records of the SDK.
    private static readonly JsonSerializerOptions SchemaJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Guidelines(Guid site) => $"{SitePrefix}{site:D}{GuidelinesSuffix}";

    public static string Instructions(string type) => $"{TypePrefix}{type}{InstructionsSuffix}";

    public async ValueTask<IReadOnlyList<McpResourceDescriptor>> ListResourcesAsync(McpListRequest request, CancellationToken cancellationToken = default)
    {
        var site = request.Operation.Site;
        List<McpResourceDescriptor> resources = [];
        var readable = await ReadableAsync(cancellationToken);
        if (readable is not null)
        {
            resources.Add(new(Schema, "schema") { Title = "Content schema", Description = "The content types, components and blocks that the caller may read, with their fields.", MimeType = Json });
            resources.Add(new(SchemaTypeTemplate, "schema-type") { Title = "Content type", Description = "One content type with the fields that the caller may read.", MimeType = Json });
        }

        if (await instructions.GuidelinesAsync(site, cancellationToken) is not null)
        {
            resources.Add(new(Guidelines(site.Value), "guidelines") { Title = "Site guidelines", Description = "How this site wants its content written. Read them before writing.", MimeType = "text/plain" });
        }

        if (readable is not null)
        {
            resources.Add(new(InstructionsTemplate, "type-instructions") { Title = "Content type instructions", Description = "How this site wants the entries of one content type written.", MimeType = "text/plain" });
            var byType = await instructions.ByTypeAsync(site, cancellationToken);
            foreach (var type in readable.Types.Where(type => byType.ContainsKey(type.Name)))
            {
                resources.Add(new(Instructions(type.Name), $"{type.Name}-instructions") { Description = $"How this site wants the entries of the type '{type.Name}' written.", MimeType = "text/plain" });
            }
        }

        return resources;
    }

    public async ValueTask<IReadOnlyList<McpResourceContent>?> ReadResourceAsync(McpResourceRead request, CancellationToken cancellationToken = default)
    {
        var uri = request.Uri;
        var site = request.Operation.Site;
        if (uri == Schema)
        {
            return Content(uri, JsonSerializer.Serialize(await schema.DescribeAsync(cancellationToken), SchemaJson), Json);
        }

        if (Segment(uri, SchemaTypePrefix, "") is { } described)
        {
            return await schema.DescribeTypeAsync(described, cancellationToken) is { } type ? Content(uri, JsonSerializer.Serialize(type, SchemaJson), Json) : null;
        }

        // The guidelines of the site of the request only: another site's are read with a request to that site.
        if (Segment(uri, SitePrefix, GuidelinesSuffix) is { } named)
        {
            return Guid.TryParse(named, out var id) && id == site.Value && await instructions.GuidelinesAsync(site, cancellationToken) is { } guidelines
                ? Content(uri, guidelines, "text/plain")
                : null;
        }

        if (Segment(uri, TypePrefix, InstructionsSuffix) is { } instructed)
        {
            return await schema.DescribeTypeAsync(instructed, cancellationToken) is not null
                && (await instructions.ByTypeAsync(site, cancellationToken)).TryGetValue(instructed, out var text)
                ? Content(uri, text, "text/plain")
                : null;
        }

        return null;
    }

    private static IReadOnlyList<McpResourceContent> Content(string uri, string text, string mimeType) => [McpResourceContent.FromText(uri, text, mimeType)];

    // The one segment between the prefix and the suffix, or null when the URI is not of that shape.
    private static string? Segment(string uri, string prefix, string suffix)
    {
        if (!uri.StartsWith(prefix, StringComparison.Ordinal) || !uri.EndsWith(suffix, StringComparison.Ordinal) || uri.Length <= prefix.Length + suffix.Length)
        {
            return null;
        }

        var segment = uri[prefix.Length..^suffix.Length];
        return segment.Contains('/', StringComparison.Ordinal) ? null : Uri.UnescapeDataString(segment);
    }

    // The schema the caller may read, or null for a caller that the service refuses the schema, which is then not listed.
    private async ValueTask<SchemaDescription?> ReadableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await schema.DescribeAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && problems.Describe(exception).Status is 401 or 403)
        {
            return null;
        }
    }
}
