using FluentCMS.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The step before an older key of the Data Protection key ring, or the certificate that protects it, is retired. It names a
// document it leaves as it is by its area and scope, and the secret keys, never a value or what is stored.
internal sealed class SettingsReprotectCommand : HostCommand
{
    public override string Name => "settings reprotect";

    public override string Description => "Protects every stored secret setting again under the current key of the Data Protection key ring; run it before retiring a ring key or certificate.";

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var result = await context.Services.GetRequiredService<SecretSettingsReprotection>().RunAsync(cancellationToken);
        await context.Output.WriteLineAsync($"Protected the secrets of {result.Documents} stored settings documents again under the current key.");
        if (result.Left.Count == 0)
        {
            return CommandExitCodes.Success;
        }

        foreach (var left in result.Left)
        {
            var scope = left.Site is { } site ? $"site {site:D}" : "the instance";
            await context.Error.WriteLineAsync(left.Keys.Count > 0
                ? $"The settings area '{left.Area}' for {scope} is left as it is: the key ring cannot unprotect {string.Join(", ", left.Keys)}."
                : $"The settings area '{left.Area}' for {scope} is left as it is: no settings class of this host has that area, so its secrets are not known.");
        }

        throw new CommandException(
            CommandErrorCodes.SecretsNotReprotected,
            $"{result.Left.Count} stored settings documents were left as they are and still need the key that protected them; restore that key or certificate, or set those secrets again, and run 'fluentcms settings reprotect' again before retiring a key");
    }
}
