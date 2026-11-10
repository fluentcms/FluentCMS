using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Agents;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Mcp;

// The template of a stored prompt. {{name}} stands for the argument of that name and is all a template can say: there
// are no expressions, and a value is put in as it is and never read as a template again.
internal static partial class McpPromptTemplate
{
    private const string Open = "{{";

    // The arguments the template names, in the order it first names them.
    public static IReadOnlyList<string> Names(string template) =>
        [.. Placeholder().Matches(template).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    // Whether {{ is left where it opens no {{name}}, so that a template reads one way only. What stood for an argument
    // becomes a space, so that the text on its two sides does not join into {{.
    public static bool HasStrayOpening(string template) => Placeholder().Replace(template, " ").Contains(Open, StringComparison.Ordinal);

    public static string Fill(string template, IReadOnlyDictionary<string, string> arguments) =>
        Placeholder().Replace(template, match => arguments.GetValueOrDefault(match.Groups[1].Value) ?? "");

    public static bool IsArgumentName(string? name) => name is not null && ArgumentName().IsMatch(name);

    [GeneratedRegex(@"\{\{([A-Za-z_][A-Za-z0-9_]{0,63})\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentName();
}

internal sealed record McpStoredArgument(string Name, string? Description, bool Required);

internal sealed record McpStoredPrompt(string Name, string? Title, string? Description, IReadOnlyList<McpStoredArgument> Arguments, string Template);

// The stored prompts of a site as its agents get them, read once per request. They are for every agent of the site, so
// nothing is checked here.
internal sealed class McpStoredPrompts(PluginDbContext<McpPlugin> database, ILogger<McpStoredPrompts> logger)
{
    private readonly Dictionary<Guid, IReadOnlyList<McpStoredPrompt>> _sites = [];

    public async ValueTask<IReadOnlyList<McpStoredPrompt>> OfSiteAsync(SiteId site, CancellationToken cancellationToken)
    {
        if (!_sites.TryGetValue(site.Value, out var prompts))
        {
            var rows = await database.Set<McpPrompt>().AsNoTracking().Where(prompt => prompt.SiteId == site.Value).ToListAsync(cancellationToken);
            List<McpStoredPrompt> read = [];
            foreach (var row in rows.OrderBy(row => row.Name, StringComparer.Ordinal))
            {
                if (ArgumentsOf(row.Arguments) is { } arguments)
                {
                    read.Add(new McpStoredPrompt(row.Name, row.Title, row.Description, arguments, row.Template));
                }
                else
                {
                    McpLog.PromptUnreadable(logger, row.Name, site.Value);
                }
            }

            _sites[site.Value] = prompts = read;
        }

        return prompts;
    }

    public static string Json(IReadOnlyList<McpStoredArgument> arguments) => JsonSerializer.Serialize(arguments, JsonSerializerOptions.Web);

    // Null for a row that holds something other than what Json writes.
    public static IReadOnlyList<McpStoredArgument>? ArgumentsOf(string json)
    {
        try
        {
            var arguments = JsonSerializer.Deserialize<List<McpStoredArgument>>(json, JsonSerializerOptions.Web);
            return arguments is not null && arguments.All(argument => argument is not null && McpPromptTemplate.IsArgumentName(argument.Name)) ? arguments : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Serves the stored prompts of the site of a request beside the prompts of the plugins' providers.
internal sealed class McpStoredPromptProvider(McpStoredPrompts prompts) : IMcpPromptProvider
{
    public async ValueTask<IReadOnlyList<McpPromptDescriptor>> ListPromptsAsync(McpListRequest request, CancellationToken cancellationToken = default) =>
    [
        .. (await prompts.OfSiteAsync(request.Operation.Site, cancellationToken)).Select(prompt => new McpPromptDescriptor(prompt.Name)
        {
            Title = prompt.Title,
            Description = prompt.Description,
            Arguments = [.. prompt.Arguments.Select(argument => new McpPromptArgument(argument.Name) { Description = argument.Description, Required = argument.Required })],
        }),
    ];

    public async ValueTask<McpPromptResult?> GetPromptAsync(McpPromptGet request, CancellationToken cancellationToken = default)
    {
        var prompt = (await prompts.OfSiteAsync(request.Operation.Site, cancellationToken)).FirstOrDefault(stored => stored.Name == request.Name);
        if (prompt is null)
        {
            return null;
        }

        if (prompt.Arguments.FirstOrDefault(argument => argument.Required && string.IsNullOrEmpty(request.Arguments.GetValueOrDefault(argument.Name))) is { } missing)
        {
            throw new PluginException(McpErrorCodes.MissingPromptArgument, $"The prompt '{prompt.Name}' needs the argument '{missing.Name}'.");
        }

        return new McpPromptResult([new McpPromptMessage(McpPromptRole.User, McpPromptTemplate.Fill(prompt.Template, request.Arguments))])
        {
            Description = prompt.Description,
        };
    }
}
