using System.Text.Json.Nodes;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

internal sealed class SchemaDocuments(IContentTypeDirectory directory, FieldTypeRegistry fieldTypes) : ISchemaDocuments
{
    public async Task<Result<JsonObject>> GetTypeAsync(string slug, CancellationToken cancellationToken = default)
    {
        var type = await directory.GetAsync(slug, cancellationToken);

        return type.IsSuccess
            ? Result<JsonObject>.Success(SchemaDocument.ForType(type.Value, fieldTypes))
            : Result<JsonObject>.Failure(type.Errors);
    }

    public async Task<JsonObject> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var types = new List<ContentType>();

        foreach (var summary in await directory.ListAsync(cancellationToken))
        {
            // A type listed a moment ago is still there: types are never deleted.
            types.Add((await directory.GetAsync(summary.Slug, cancellationToken)).Value);
        }

        return SchemaDocument.ForSite(types, fieldTypes);
    }
}
