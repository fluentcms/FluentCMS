using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// What the commands that administer an instance share: their options, the names they take for roles and sites, the
// password they read, and their output, which is the shape of the Management API with --json and a table otherwise.
internal static class AdminCommandSupport
{
    public const string Json = "--json";
    public const string Email = "--email";
    public const string Role = "--role";
    public const string Site = "--site";
    public const string PasswordStdin = "--password-stdin";

    public static CommandOption JsonOption { get; } = new(Json, "Writes the result as one line of JSON, in the shape of the Management API.");

    public static CommandOption SiteOption { get; } = new(Site, "The slug of the site whose role is meant; an instance-wide role when left out.", "slug");

    public static CommandOption PasswordStdinOption { get; } = new(PasswordStdin, "Reads the password from the first line of standard input instead of asking for it.");

    public static string Required(CommandContext context, string option) =>
        context.Arguments.Value(option) is { Length: > 0 } value
            ? value
            : throw new CommandException(CommandErrorCodes.BadUsage, $"{option} is required", CommandExitCodes.BadUsage);

    public static Guid Id(string value, string name) =>
        Guid.TryParse(value, out var id)
            ? id
            : throw new CommandException(CommandErrorCodes.InvalidValue, $"<{name}> is the id of the item, such as 0190a3c4-5e6f-7a8b-9c0d-1e2f3a4b5c6d", CommandExitCodes.BadUsage);

    // The site of a slug; null when none is given.
    public static async Task<Guid?> SiteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Value(Site) is not { } slug)
        {
            return null;
        }

        var sites = await context.Services.GetRequiredService<SiteManager>().ListAsync(cancellationToken);
        return sites.FirstOrDefault(site => string.Equals(site.Slug, slug, StringComparison.OrdinalIgnoreCase))?.Id
            ?? throw new CommandException(CommandErrorCodes.UnknownSite, $"there is no site '{slug}'; the sites are {string.Join(", ", sites.Select(site => site.Slug))}", CommandExitCodes.BadUsage);
    }

    // A role by its name, among the roles of the site, or the instance-wide ones when no site is given. A key's role may also
    // be instance-wide when a site is given, since the key's binding to the site narrows it.
    public static async Task<Guid> RoleAsync(CommandContext context, Guid? site, bool instanceWideToo, CancellationToken cancellationToken)
    {
        var name = Required(context, Role);
        var roles = await context.Services.GetRequiredService<RoleService>().ListAsync(cancellationToken);
        var found = Named(roles.Where(role => role.SiteId == site), name)
            ?? (instanceWideToo && site is not null ? Named(roles.Where(role => role.SiteId is null), name) : null);
        if (found is { } role)
        {
            return role.Id;
        }

        var candidates = roles.Where(role => role.SiteId == site || (instanceWideToo && role.SiteId is null)).Select(role => role.Name).Distinct();
        throw new CommandException(
            CommandErrorCodes.UnknownRole,
            $"there is no role '{name}' {(site is null ? "for the instance" : "in the site")}; the roles are {string.Join(", ", candidates)}",
            CommandExitCodes.BadUsage);
    }

    // The password from standard input with --password-stdin, or asked for twice at the terminal. It is never an argument,
    // which a process list or a shell history would keep, and it is never written out.
    public static async Task<string> PasswordAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Has(PasswordStdin))
        {
            var line = await context.Input.ReadLineAsync(cancellationToken);
            return string.IsNullOrEmpty(line)
                ? throw new CommandException(CommandErrorCodes.PasswordNotRead, "standard input held no password on its first line")
                : line;
        }

        if (!context.Input.IsTerminal)
        {
            throw new CommandException(
                CommandErrorCodes.PasswordNotRead,
                $"standard input is not a terminal, so no password can be asked for; pass {PasswordStdin} and write the password to standard input",
                CommandExitCodes.BadUsage);
        }

        var password = await context.Input.ReadSecretAsync(context.Error, "Password: ", cancellationToken);
        if (string.IsNullOrEmpty(password))
        {
            throw new CommandException(CommandErrorCodes.PasswordNotRead, "no password was typed");
        }

        var again = await context.Input.ReadSecretAsync(context.Error, "Repeat the password: ", cancellationToken);
        return password == again
            ? password
            : throw new CommandException(CommandErrorCodes.PasswordNotRead, "the two passwords differ");
    }

    // FluentCms:PublicOrigin, which the link in an invitation or a reset email is built from, since a command has no request.
    public static Uri Link(CommandContext context, string path) =>
        context.Services.GetRequiredService<PublicOrigin>().Base is { } origin
            ? new Uri(origin + path, UriKind.Absolute)
            : throw new CommandException(
                CommandErrorCodes.PublicOriginRequired,
                $"{FluentCmsOptions.Section}:{PublicOrigin.Key} is not set, and the link in the email is built from it; set it to the address users reach the instance at");

    public static string ToJson(object value) => JsonSerializer.Serialize(value, AdminJson.Options);

    public static async Task WriteTableAsync(TextWriter output, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var all = rows.ToList();
        var widths = headers.Select((header, column) => all.Select(row => row[column].Length).Append(header.Length).Max()).ToList();
        await output.WriteLineAsync(Line(headers, widths));
        foreach (var row in all)
        {
            await output.WriteLineAsync(Line(row, widths));
        }
    }

    public static string Moment(DateTimeOffset? value) =>
        value is { } moment ? moment.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "-";

    // The first line of a text, cut to fit a column.
    public static string Short(string? text, int length = 60)
    {
        var line = (text ?? "-").Split('\n')[0].Trim();
        return line.Length <= length ? line : string.Concat(line.AsSpan(0, length - 3), "...");
    }

    private static RoleInfo? Named(IEnumerable<RoleInfo> roles, string name)
    {
        var candidates = roles.ToList();
        var exact = candidates.FirstOrDefault(role => role.Name == name);
        var loose = candidates.Where(role => string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact ?? (loose.Count == 1 ? loose[0] : null);
    }

    private static string Line(IReadOnlyList<string> cells, List<int> widths)
    {
        var line = new StringBuilder();
        for (var column = 0; column < cells.Count; column++)
        {
            line.Append(column == cells.Count - 1 ? cells[column] : cells[column].PadRight(widths[column] + 2));
        }

        return line.ToString().TrimEnd();
    }
}
