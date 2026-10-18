using System.Text.Json.Nodes;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

// The schema in a form an agent can write entries from without other input (SCH-06).
public interface ISchemaDocuments
{
    // The JSON Schema for entries of one content type, at its current version.
    Task<Result<JsonObject>> GetTypeAsync(string slug, CancellationToken cancellationToken = default);

    // Every content type of the site under $defs.
    Task<JsonObject> GetSiteAsync(CancellationToken cancellationToken = default);
}
