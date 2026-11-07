namespace FluentCMS.Diagnostics;

/// <summary>A provider slot and the implementation the host resolves it to.</summary>
public sealed record StartupProviderSlot
{
    /// <summary>The full name of the slot's type, such as <c>FluentCMS.Providers.IEmailSender</c>.</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>The full name of the type in use, or <see langword="null"/> when it could not be created.</summary>
    public string? Implementation { get; init; }

    /// <summary>
    /// Who chose it: <c>default</c> for the slot's default, <c>host</c> and <c>plugin</c> for a replacement through
    /// <c>Providers.Use</c>, and <c>services</c> for a service registered for the slot directly.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>The name of the plugin when <see cref="Source"/> is <c>plugin</c>.</summary>
    public string? Plugin { get; init; }
}
