using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FluentCMS.Cli;

// The commands that reach a running instance with --url. They call the Management API as the key's principal, so the API
// decides what the key may do; they print what the same command prints inside a host, and --json is the API's answer.
internal sealed class RemoteCommands(ManagementClient client, TextWriter output, TextWriter error)
{
    public async Task<int> ApiKeyCreateAsync(string name, string keyClass, string role, string? site, IReadOnlyList<string>? types, DateTimeOffset? expires, bool json, CancellationToken cancellationToken)
    {
        var siteId = await SiteAsync(site, cancellationToken);
        var roleId = await RoleAsync(role, siteId, instanceWideToo: true, cancellationToken);
        var created = await client.SendAsync(
            HttpMethod.Post,
            "api-keys",
            new { name, @class = keyClass, roleId, siteIds = siteId is { } one ? new[] { one } : null, typeNames = types, expiresAt = expires },
            cancellationToken) ?? throw Unexpected("POST api-keys");

        if (json)
        {
            await WriteJsonAsync(created);
            return ExitCodes.Success;
        }

        await output.WriteLineAsync(ManagementClient.Text(created, "secret") ?? throw Unexpected("POST api-keys"));
        await error.WriteLineAsync($"Created the {keyClass} key '{ManagementClient.Text(created, "name")}' ({ManagementClient.Text(created, "id")}). The key above is shown only once; keep it in a secret store.");
        return ExitCodes.Success;
    }

    public async Task<int> ApiKeyListAsync(bool json, CancellationToken cancellationToken)
    {
        var keys = await client.GetAsync("api-keys", cancellationToken);
        if (json)
        {
            await WriteJsonAsync(keys);
            return ExitCodes.Success;
        }

        var now = DateTimeOffset.UtcNow;
        await WriteTableAsync(
            ["ID", "NAME", "CLASS", "PREFIX", "STATUS", "EXPIRES", "LAST USED"],
            Items(keys).Select(key => (IReadOnlyList<string>)
            [
                Text(key, "id"),
                Text(key, "name"),
                Text(key, "class"),
                Text(key, "keyPrefix"),
                Time(key, "revokedAt") is not null ? "revoked" : Time(key, "expiresAt") <= now ? "expired" : "active",
                Moment(Time(key, "expiresAt")),
                Moment(Time(key, "lastUsedAt")),
            ]));
        return ExitCodes.Success;
    }

    public async Task<int> ApiKeyRevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        await client.SendAsync(HttpMethod.Delete, $"api-keys/{id:D}", null, cancellationToken);
        await output.WriteLineAsync($"The API key {id:D} is revoked.");
        return ExitCodes.Success;
    }

    // The link in the email is built by the instance from its public origin.
    public async Task<int> UserInviteAsync(string email, string role, string? site, CancellationToken cancellationToken)
    {
        var roleId = await RoleAsync(role, await SiteAsync(site, cancellationToken), instanceWideToo: false, cancellationToken);
        var sent = await client.SendAsync(HttpMethod.Post, "invitations", new { email, roleIds = new[] { roleId } }, cancellationToken) ?? throw Unexpected("POST invitations");
        await output.WriteLineAsync($"Invited {Text(sent, "email")}; the invitation {Text(sent, "id")} expires at {Moment(Time(sent, "expiresAt"))} UTC.");
        return ExitCodes.Success;
    }

    public async Task<int> UserAddRoleAsync(string email, string role, string? site, CancellationToken cancellationToken)
    {
        var roleId = await RoleAsync(role, await SiteAsync(site, cancellationToken), instanceWideToo: false, cancellationToken);
        var users = await client.GetAsync($"users?search={Uri.EscapeDataString(email)}&pageSize=100", cancellationToken);
        var user = Items(users).FirstOrDefault(item => string.Equals(ManagementClient.Text(item, "email"), email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user.ValueKind != JsonValueKind.Object)
        {
            throw new CliException(CliErrorCodes.UserNotFound, $"there is no user with the address {email}.");
        }

        await client.SendAsync(HttpMethod.Put, $"users/{Text(user, "id")}/roles/{roleId:D}", null, cancellationToken);
        await output.WriteLineAsync($"Gave {Text(user, "userName")} the role {role}.");
        return ExitCodes.Success;
    }

    public async Task<int> JobsListAsync(string? queue, string? status, bool json, CancellationToken cancellationToken)
    {
        var query = new StringBuilder("jobs?pageSize=100");
        if (queue is not null)
        {
            query.Append("&queue=").Append(Uri.EscapeDataString(queue));
        }

        if (status is not null)
        {
            query.Append("&status=").Append(Uri.EscapeDataString(status));
        }

        var jobs = await client.GetAsync(query.ToString(), cancellationToken);
        if (json)
        {
            await WriteJsonAsync(jobs);
            return ExitCodes.Success;
        }

        var items = Items(jobs).ToList();
        await WriteTableAsync(
            ["ID", "TYPE", "QUEUE", "STATUS", "ATTEMPTS", "RUN AT", "LAST ERROR"],
            items.Select(job => (IReadOnlyList<string>)
            [
                Text(job, "id"),
                Text(job, "type"),
                Text(job, "queue"),
                Text(job, "status"),
                $"{Number(job, "attempts")}/{Number(job, "maxAttempts")}",
                Moment(Time(job, "runAt")),
                Short(ManagementClient.Text(job, "lastError")),
            ]));
        if (jobs.TryGetProperty("total", out var total) && total.TryGetInt64(out var count) && count > items.Count)
        {
            await error.WriteLineAsync($"The newest {items.Count} of {count} jobs are shown.");
        }

        return ExitCodes.Success;
    }

    public async Task<int> JobsRetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await client.SendAsync(HttpMethod.Post, $"jobs/{id:D}/retry", null, cancellationToken) ?? throw Unexpected("POST jobs/retry");
        await output.WriteLineAsync($"Queued the job {Text(job, "id")} ({Text(job, "type")}) again on the queue {Text(job, "queue")}.");
        return ExitCodes.Success;
    }

    // The sites the key holds a grant in, which are the ones it can bind a key or a role to.
    private async Task<Guid?> SiteAsync(string? slug, CancellationToken cancellationToken)
    {
        if (slug is null)
        {
            return null;
        }

        var sites = Items(await client.GetAsync("me/sites", cancellationToken)).ToList();
        var site = sites.FirstOrDefault(item => string.Equals(ManagementClient.Text(item, "slug"), slug, StringComparison.OrdinalIgnoreCase));
        return site.ValueKind == JsonValueKind.Object && Guid.TryParse(ManagementClient.Text(site, "id"), out var id)
            ? id
            : throw new CliException(CliErrorCodes.UnknownSite, $"there is no site '{slug}' that the key reaches; the sites are {string.Join(", ", sites.Select(item => Text(item, "slug")))}", ExitCodes.BadUsage);
    }

    // As inside the host: a role of the site, or an instance-wide one when no site is given; a key's role may be instance-wide
    // when a site is given too.
    private async Task<Guid> RoleAsync(string name, Guid? site, bool instanceWideToo, CancellationToken cancellationToken)
    {
        var roles = Items(await client.GetAsync("roles", cancellationToken))
            .Select(role => (Id: ManagementClient.Text(role, "id"), Site: Guid.TryParse(ManagementClient.Text(role, "siteId"), out var id) ? id : (Guid?)null, Name: Text(role, "name")))
            .ToList();
        var found = Named(roles.Where(role => role.Site == site), name)
            ?? (instanceWideToo && site is not null ? Named(roles.Where(role => role.Site is null), name) : null);
        if (found is { } role && Guid.TryParse(role.Id, out var roleId))
        {
            return roleId;
        }

        var candidates = roles.Where(role => role.Site == site || (instanceWideToo && role.Site is null)).Select(role => role.Name).Distinct();
        throw new CliException(
            CliErrorCodes.UnknownRole,
            $"there is no role '{name}' {(site is null ? "for the instance" : "in the site")}; the roles are {string.Join(", ", candidates)}",
            ExitCodes.BadUsage);
    }

    private static (string? Id, Guid? Site, string Name)? Named(IEnumerable<(string? Id, Guid? Site, string Name)> roles, string name)
    {
        var candidates = roles.ToList();
        var exact = candidates.Where(role => role.Name == name).ToList();
        var loose = candidates.Where(role => string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0 ? exact[0] : loose.Count == 1 ? loose[0] : null;
    }

    private async Task WriteJsonAsync(JsonElement value) => await output.WriteLineAsync(JsonSerializer.Serialize(value));

    private async Task WriteTableAsync(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var all = rows.ToList();
        var widths = headers.Select((header, column) => all.Select(row => row[column].Length).Append(header.Length).Max()).ToList();
        foreach (var row in all.Prepend(headers))
        {
            var line = new StringBuilder();
            for (var column = 0; column < row.Count; column++)
            {
                line.Append(column == row.Count - 1 ? row[column] : row[column].PadRight(widths[column] + 2));
            }

            await output.WriteLineAsync(line.ToString().TrimEnd());
        }
    }

    private CliException Unexpected(string call) => new(CliErrorCodes.UnexpectedAnswer, $"{client.Instance} answered {call} with no body.");

    private static List<JsonElement> Items(JsonElement list) =>
        list.ValueKind == JsonValueKind.Object && list.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? [.. items.EnumerateArray()] : [];

    private static string Text(JsonElement element, string name) => ManagementClient.Text(element, name) ?? "-";

    private static string Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number.ToString(CultureInfo.InvariantCulture) : "-";

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time) ? time : null;

    private static string Moment(DateTimeOffset? value) =>
        value is { } moment ? moment.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "-";

    private static string Short(string? text, int length = 60)
    {
        var line = (text ?? "-").Split('\n')[0].Trim();
        return line.Length <= length ? line : string.Concat(line.AsSpan(0, length - 3), "...");
    }
}
