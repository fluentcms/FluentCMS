using FluentCMS.Core.Settings;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The step before an older key of the Data Protection key ring, or the certificate that protects it, is retired. It names
// the default key it protected under, which is not secret, and a document it leaves as it is by its area and scope and the
// keys, never a value or what is stored. It fails only when a stored value still needs an older key.
internal sealed class SettingsReprotectCommand : HostCommand
{
    public override string Name => "settings reprotect";

    public override string Description => "Protects every stored secret setting again under the current default key of the Data Protection key ring and names that key; run it before retiring a ring key or certificate.";

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        // The key the pass protects under, read before it, as Data Protection reads it for each value.
        var key = context.Services.GetRequiredService<IKeyRingProvider>().GetCurrentKeyRing().DefaultKeyId;
        var result = await context.Services.GetRequiredService<SecretSettingsReprotection>().RunAsync(cancellationToken);
        await context.Output.WriteLineAsync($"Protected the secrets of {result.Documents} stored settings documents again under the default key {key:D} of the key ring. Retire an older key only once this is the key you keep and every instance protects under it.");

        foreach (var left in result.Left)
        {
            var scope = left.Site is { } site ? $"site {site:D}" : "the instance";
            if (!left.Registered)
            {
                var older = left.Unreached.Count > 0 ? $"; {string.Join(", ", left.Unreached)} hold a protected value under an older key or one the key ring cannot unprotect" : "";
                await context.Error.WriteLineAsync($"The settings area '{left.Area}' for {scope} is left as it is: no settings class of this host has that area, so the command cannot tell which of its values are secrets, nor whether the plugin that owns it is only not loaded in this run{older}.");
                continue;
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

        var pending = result.Left.Where(left => left.Unreadable.Count > 0 || left.Unreached.Count > 0).ToList();
        if (pending.Count == 0)
        {
            return CommandExitCodes.Success;
        }

        var unreadable = pending.Count(left => left.Unreadable.Count > 0);
        var unreached = pending.Count(left => left.Registered && left.Unreached.Count > 0);
        var unregistered = pending.Count(left => !left.Registered);
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
            advice.Add($"{unregistered} are of areas no settings class of this host has: run the command in a host that registers them, such as with the plugin that owns the area");
        }

        throw new CommandException(
            CommandErrorCodes.SecretsNotReprotected,
            $"{pending.Count} stored settings documents still need the key that protected them; {string.Join("; ", advice)}; then run 'fluentcms settings reprotect' again before retiring a key");
    }
}
