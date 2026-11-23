using FluentCMS.Core.Settings;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The step before an older key of the Data Protection key ring, or the certificate that protects it, is retired. It names
// the default key it protected under, which is not secret, and a document it leaves as it is by its area and scope and the
// keys, never a value or what is stored.
internal sealed class SettingsReprotectCommand : HostCommand
{
    private const string RemoveArea = "--remove-area";

    public override string Name => "settings reprotect";

    public override string Description => "Protects every stored secret setting again under the current default key of the Data Protection key ring and names that key; run it before retiring a ring key or certificate.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(RemoveArea, "First removes every stored document of this area, which no settings class of this host may have, such as that of a plugin the host no longer runs.", "area"),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var pass = context.Services.GetRequiredService<SecretSettingsReprotection>();
        if (context.Arguments.Value(RemoveArea) is { } area)
        {
            await RemoveAsync(context, pass, area, cancellationToken);
        }

        // The key the pass protects under, read before it, as Data Protection reads it for each value.
        var key = context.Services.GetRequiredService<IKeyRingProvider>().GetCurrentKeyRing().DefaultKeyId;
        var result = await pass.RunAsync(cancellationToken);
        await context.Output.WriteLineAsync($"Protected the secrets of {result.Documents} stored settings documents again under the default key {key:D} of the key ring. Retire an older key only once this is the key you keep and every instance protects under it.");
        if (result.Left.Count == 0)
        {
            return CommandExitCodes.Success;
        }

        foreach (var left in result.Left)
        {
            var scope = Scope(left.Site);
            if (!left.Registered)
            {
                await context.Error.WriteLineAsync($"The settings area '{left.Area}' for {scope} is left as it is: no settings class of this host has that area, so its secrets are not known.");
            }

            if (left.Unreadable.Count > 0)
            {
                await context.Error.WriteLineAsync($"The settings area '{left.Area}' for {scope} is left as it is: the key ring cannot unprotect {string.Join(", ", left.Unreadable)}.");
            }

            if (left.Unreached.Count > 0)
            {
                await context.Error.WriteLineAsync($"The settings area '{left.Area}' for {scope} holds a protected value under {string.Join(", ", left.Unreached)}, which its settings class does not read as a secret, under an older key or one the key ring cannot unprotect; the pass leaves it as it is.");
            }
        }

        var unreadable = result.Left.Count(left => left.Unreadable.Count > 0);
        var unreached = result.Left.Count(left => left.Unreached.Count > 0);
        var unregistered = result.Left.Count(left => !left.Registered);
        List<string> advice = [];
        if (unreadable > 0)
        {
            advice.Add($"{unreadable} hold secrets the key ring cannot unprotect: restore the key or certificate that protected them, or set those secrets again");
        }

        if (unreached > 0)
        {
            advice.Add($"{unreached} hold protected values under keys their settings class does not read as secrets: write those areas again without those keys");
        }

        if (unregistered > 0)
        {
            advice.Add($"{unregistered} are of areas no settings class of this host has: run the command in a host that registers them, such as with the plugin that owns the area, or remove them with {RemoveArea} <area>");
        }

        throw new CommandException(
            CommandErrorCodes.SecretsNotReprotected,
            $"{result.Left.Count} stored settings documents still need the key that protected them; {string.Join("; ", advice)}; then run 'fluentcms settings reprotect' again before retiring a key");
    }

    private static async Task RemoveAsync(CommandContext context, SecretSettingsReprotection pass, string area, CancellationToken cancellationToken)
    {
        if (context.Services.GetRequiredService<SettingsRegistry>().Find(area) is not null)
        {
            throw new CommandException(CommandErrorCodes.InvalidValue, $"{RemoveArea} names '{area}', which a settings class of this host has; reset it through the Management API instead", CommandExitCodes.BadUsage);
        }

        var removed = await pass.RemoveUnregisteredAsync(area, cancellationToken);
        if (removed.Count == 0)
        {
            await context.Output.WriteLineAsync($"No stored settings document has the area '{area}'.");
            return;
        }

        foreach (var site in removed)
        {
            await context.Output.WriteLineAsync($"Removed the settings area '{area}' for {Scope(site)}.");
        }
    }

    private static string Scope(Guid? site) => site is { } one ? $"site {one:D}" : "the instance";
}
