using System.Globalization;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Identity.ApiKeys;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Core.Commands.AdminCommandSupport;

namespace FluentCMS.Core.Commands;

// `apikey create`: the key goes to standard output once, alone on its line, so that a script can keep it; what describes it
// goes to standard error. With --json the line is the Management API's answer, which holds the key as `secret`.
internal sealed class ApiKeyCreateCommand : HostCommand
{
    public const string KeyName = "--name";
    public const string Class = "--class";
    public const string Types = "--types";
    public const string Expires = "--expires";

    public override string Name => "apikey create";

    public override string Description => "Creates an API key bound to a role and prints the key once.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(KeyName, "The name of the key, which lists and the activity log show.", "name"),
        new(Class, "delivery (published content), preview (drafts too) or management (what the role allows).", "class"),
        new(Role, "The role the key acts with.", "name"),
        new(Site, "The slug of the one site the key is bound to; every site when left out. The role may be the site's or instance-wide.", "slug"),
        new(Types, "The content types the key is bound to, separated by commas; every type when left out.", "list"),
        new(Expires, "When the key expires, such as 2027-06-30 or 2027-06-30T12:00:00Z (UTC unless an offset is given); never when left out.", "date"),
        JsonOption,
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var name = Required(context, KeyName);
        var keyClass = ClassOf(Required(context, Class));
        var types = TypesOf(context.Arguments.Value(Types));
        var expires = ExpiryOf(context.Arguments.Value(Expires));
        var site = await SiteAsync(context, cancellationToken);
        var role = await RoleAsync(context, site, instanceWideToo: true, cancellationToken);

        var created = await context.Services.GetRequiredService<ApiKeyService>().CreateAsync(
            new ApiKeyRequest(name, keyClass, role) { SiteIds = site is { } one ? [one] : null, TypeNames = types, ExpiresAt = expires },
            cancellationToken);

        if (context.Arguments.Has(Json))
        {
            await context.Output.WriteLineAsync(ToJson(CreatedApiKeyResponse.From(created)));
            return CommandExitCodes.Success;
        }

        await context.Output.WriteLineAsync(created.Secret);
        await context.Error.WriteLineAsync($"Created the {keyClass.ToString().ToLowerInvariant()} key '{created.Key.Name}' ({created.Key.Id:D}). The key above is shown only once; keep it in a secret store.");
        return CommandExitCodes.Success;
    }

    private static ApiKeyClass ClassOf(string value) => value switch
    {
        "delivery" => ApiKeyClass.Delivery,
        "preview" => ApiKeyClass.Preview,
        "management" => ApiKeyClass.Management,
        _ => throw new CommandException(CommandErrorCodes.InvalidValue, $"{Class} is delivery, preview or management", CommandExitCodes.BadUsage),
    };

    private static List<string>? TypesOf(string? value)
    {
        if (value is null)
        {
            return null;
        }

        List<string> types = [.. value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        return types.Count > 0
            ? types
            : throw new CommandException(CommandErrorCodes.InvalidValue, $"{Types} names at least one content type; leave it out for every type", CommandExitCodes.BadUsage);
    }

    private static DateTimeOffset? ExpiryOf(string? value) =>
        value is null ? null
        : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires) ? expires
        : throw new CommandException(CommandErrorCodes.InvalidValue, $"{Expires} is a date such as 2027-06-30, or a time such as 2027-06-30T12:00:00Z", CommandExitCodes.BadUsage);
}

internal sealed class ApiKeyListCommand : HostCommand
{
    public override string Name => "apikey list";

    public override string Description => "Lists the API keys, revoked and expired ones included; a key itself is never shown again.";

    public override IReadOnlyList<CommandOption> Options { get; } = [JsonOption];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var keys = await context.Services.GetRequiredService<ApiKeyService>().ListAsync(cancellationToken);
        if (context.Arguments.Has(Json))
        {
            await context.Output.WriteLineAsync(ToJson(new ItemList<ApiKeyInfo>(keys)));
            return CommandExitCodes.Success;
        }

        var now = context.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await WriteTableAsync(
            context.Output,
            ["ID", "NAME", "CLASS", "PREFIX", "STATUS", "EXPIRES", "LAST USED"],
            keys.Select(key => (IReadOnlyList<string>)
            [
                key.Id.ToString("D"),
                key.Name,
                key.Class.ToString().ToLowerInvariant(),
                key.KeyPrefix,
                key.RevokedAt is not null ? "revoked" : key.ExpiresAt <= now ? "expired" : "active",
                Moment(key.ExpiresAt),
                Moment(key.LastUsedAt),
            ]));
        return CommandExitCodes.Success;
    }
}

internal sealed class ApiKeyRevokeCommand : HostCommand
{
    public override string Name => "apikey revoke";

    public override string Description => "Revokes an API key, which is refused from then on and stays in the list.";

    public override IReadOnlyList<string> Arguments { get; } = ["id"];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var id = Id(context.Arguments.Positional[0], "id");
        var revoked = await context.Services.GetRequiredService<ApiKeyService>().RevokeAsync(id, cancellationToken);
        await context.Output.WriteLineAsync(revoked ? $"Revoked the API key {id:D}." : $"The API key {id:D} was revoked already.");
        return CommandExitCodes.Success;
    }
}
