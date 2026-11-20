using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Api;

// One save of an entry in one locale, as GET /content/{type}/{id}/versions lists it (spec 4.11). The author is named by its
// kind (adminUser, member, apiKey, serviceAccount, system, anonymous) and the name it had at the time; the origin is where the
// save came from (admin, api, mcp, job, cli, seed, import).
internal sealed record EntryVersionInfo
{
    public int Number { get; init; }

    public string? Locale { get; init; }

    public string? Status { get; init; }

    public Guid AuthorId { get; init; }

    public string? AuthorKind { get; init; }

    public string? AuthorName { get; init; }

    public string? Origin { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

// GET .../versions/{from}/diff/{to}: the changes over what the caller may read of both versions, as stored values.
internal sealed record VersionDiffInfo
{
    public int From { get; init; }

    public int To { get; init; }

    public IReadOnlyList<VersionChangeInfo> Changes { get; init; } = [];
}

// Kind is added, removed or changed; a path such as sections[1].heading or body.content[0].content[0].text.
internal sealed record VersionChangeInfo
{
    public string Path { get; init; } = "";

    public string Kind { get; init; } = "";

    public JsonNode? Before { get; init; }

    public JsonNode? After { get; init; }
}
