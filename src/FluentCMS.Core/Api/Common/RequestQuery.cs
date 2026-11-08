using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// The query string of a request as a ContentQuery, through the grammar of decision 21. The bridge has already read the
// site and the locale, so the parser never sees site and the locale is the one the bridge resolved.
internal static class RequestQuery
{
    private static readonly string[] ListOnly = ["q", "sort", "page", "pageSize", "offset", "limit", "cursor", "withTotal"];

    // What a read of one entry takes from a query parsed with single set.
    public static GetOptions Options(ContentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new GetOptions
        {
            Status = query.Status ?? QueryStatus.Published,
            Locale = query.Locale,
            Fallback = query.Fallback,
            Select = query.Select,
            Populate = query.Populate,
            PopulateDepth = query.PopulateDepth,
            RichTextFormat = query.RichTextFormat,
        };
    }

    // A single entry has no list to filter, sort or page, so those parameters are refused rather than ignored.
    public static ContentQuery Parse(HttpRequest request, ApiRequest api, bool single)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(api);

        var parameters = request.Query
            .Where(parameter => parameter.Key != RequestBridge.SiteParameter)
            .SelectMany(parameter => parameter.Value.Select(value => KeyValuePair.Create(parameter.Key, value ?? "")))
            .ToList();

        if (single)
        {
            var refused = parameters
                .Select(parameter => parameter.Key)
                .Where(key => ListOnly.Contains(key, StringComparer.Ordinal) || key == "filter" || key.StartsWith("filter[", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(key => new ValidationError(key, QueryErrorCodes.InvalidParameter, $"'{key}' does not apply to a single entry."))
                .ToList();
            if (refused.Count > 0)
            {
                throw new ValidationException(refused);
            }
        }

        return BracketQueryParser.Parse(parameters, keepSources: true) with { Locale = api.Locale };
    }
}
