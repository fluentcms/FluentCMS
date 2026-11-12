using FluentCMS.Content;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// What export and import share inside the host: the options both take, the site they run in, and the refusals of the
// transfer services, which carry the same codes as the endpoints' problems.
internal static class TransferCommands
{
    public const string Site = "--site";
    public const string Types = "--types";

    public static CommandOption SiteOption(string verb) => new(Site, $"The site to {verb}, by slug or id; the default site when left out.", "slug");

    public static IReadOnlyList<string>? TypesOf(CommandArguments arguments) =>
        arguments.Value(Types) is { } list ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;

    // The command runs as the system in the default site; --site moves it to another, until the result is disposed.
    public static async Task<IDisposable?> EnterSiteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Value(Site) is not { } named)
        {
            return null;
        }

        var site = await Refusing(() => context.Services.GetRequiredService<ISiteResolver>().ResolveAsync(new SiteRequest { Header = named }, cancellationToken).AsTask());
        return SystemOperation.Begin(context.Services.GetRequiredService<IOperationContextAccessor>(), Origin.Cli, site);
    }

    // A refusal of the whole transfer, such as a package that is no package or a schema the instance cannot hold, with the
    // code of each error first, as the endpoint's problem has them.
    public static async Task<T> Refusing<T>(Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (ValidationException exception) when (exception.Errors.Count > 0)
        {
            var first = exception.Errors[0];
            var rest = exception.Errors.Skip(1).Select(error => $"{Environment.NewLine}{error.Code}: {Message(error)}");
            throw new CommandException(first.Code, Message(first) + string.Concat(rest));
        }
    }

    private static string Message(ValidationError error) => error.Path.Length == 0 ? error.Message : $"{error.Path}: {error.Message}";
}
