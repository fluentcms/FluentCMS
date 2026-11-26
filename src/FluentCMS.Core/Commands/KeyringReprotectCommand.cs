using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Commands;

// The step before the previous key ring certificate is removed (#3584): settings reprotect's pass, then every value the
// plugins name. The token keys of cms_signing_key leave the older ring keys by rotation (ADR 0008) and are only counted,
// and as long as one of them still needs an older key the command fails, since the previous certificate is still needed.
internal sealed class KeyringReprotectCommand : HostCommand
{
    public override string Name => "keyring reprotect";

    public override string Description => "Protects every stored value that Data Protection protects in place, the secret settings, webhook secrets and headers and the media signing key, again under the current default key of the key ring and names that key, and counts the token keys still under an older one; run it before removing a previous ring certificate.";

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var key = context.Services.GetRequiredService<IKeyRingProvider>().GetCurrentKeyRing().DefaultKeyId;
        var settings = await context.Services.GetRequiredService<SecretSettingsReprotection>().RunAsync(cancellationToken);
        var values = await context.Services.GetRequiredService<ProtectedValueReprotection>().RunAsync(cancellationToken);
        await context.Output.WriteLineAsync($"Protected the secrets of {settings.Documents} stored settings documents and {values.Values} other stored values again under the default key {key:D} of the key ring. Retire an older key only once this is the key you keep and every instance protects under it.");

        var (documents, settingsAdvice) = await SettingsReprotectCommand.ReportAsync(settings, context.Error);
        foreach (var left in values.Left)
        {
            await context.Error.WriteLineAsync($"The value '{left.Column}' of {left.Table} row {left.Id} is left as it is: the key ring cannot unprotect it.");
        }

        var tokenKeys = values.TokenKeys;
        if (tokenKeys.Older > 0)
        {
            await context.Error.WriteLineAsync($"{tokenKeys.Older} accepted keys of {StorageNames.SigningKey} are under an older key of the key ring; they are not protected again in place but replaced by rotation.");
        }

        if (tokenKeys.Unreadable > 0)
        {
            await context.Error.WriteLineAsync($"{tokenKeys.Unreadable} accepted keys of {StorageNames.SigningKey} are under a key the key ring cannot unprotect.");
        }

        List<string> pending = [];
        List<string> advice = [.. settingsAdvice.Select(item => $"of the settings documents, {item}")];
        if (documents > 0)
        {
            pending.Add($"{documents} stored settings documents");
        }

        if (values.Left.Count > 0)
        {
            pending.Add($"{values.Left.Count} other stored values");
            advice.Add($"{values.Left.Count} other stored values are under a key the key ring cannot unprotect: restore the key or certificate that protected them, or set those values again");
        }

        if (tokenKeys.Older + tokenKeys.Unreadable > 0)
        {
            pending.Add($"{tokenKeys.Older + tokenKeys.Unreadable} token keys");
        }

        if (tokenKeys.Older > 0)
        {
            advice.Add($"{tokenKeys.Older} token keys leave the older key by rotation: run 'fluentcms keys rotate' and keep the older key until 'fluentcms keys list' shows no key made before that run");
        }

        if (tokenKeys.Unreadable > 0)
        {
            advice.Add($"{tokenKeys.Unreadable} token keys are under a key the key ring cannot unprotect: restore the key or certificate that protected them, or run 'fluentcms keys revoke --force'");
        }

        if (pending.Count == 0)
        {
            return CommandExitCodes.Success;
        }

        throw new CommandException(
            CommandErrorCodes.SecretsNotReprotected,
            $"{string.Join(", ", pending)} still need the key that protected them; {string.Join("; ", advice)}; then run 'fluentcms keyring reprotect' again before retiring a key");
    }
}
