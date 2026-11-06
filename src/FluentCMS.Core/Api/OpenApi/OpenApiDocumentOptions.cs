using System.Text.Json.Nodes;

namespace FluentCMS.Core.Api.OpenApi;

// FluentCms:OpenApi. Enabled decides whether MapFluentCmsOpenApi maps the document and the Scalar UI: unset, only in
// Development, since the document names every type and field of the schema to whoever can reach it.
public sealed class OpenApiDocumentOptions
{
    public const string Section = "OpenApi";

    private readonly Dictionary<Type, JsonObject> _examples = [];

    public bool? Enabled { get; set; }

    // Examples are keyed by the class of a content type, a component or a block, and are not read from configuration.
    internal IReadOnlyDictionary<Type, JsonObject> Examples => _examples;

    // An example of an entry, a component or a block, as the API writes it, for the schema of that class.
    public OpenApiDocumentOptions AddExample<T>(JsonObject example)
    {
        ArgumentNullException.ThrowIfNull(example);

        _examples[typeof(T)] = example;
        return this;
    }
}
