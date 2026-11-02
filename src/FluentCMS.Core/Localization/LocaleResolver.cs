using FluentCMS.Localization;

namespace FluentCMS.Core.Localization;

internal sealed class LocaleResolver(LocaleRegistry registry) : ILocaleResolver
{
    public async ValueTask<string> ResolveAsync(LocaleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requested = Present(request.Parameter) ?? Present(request.Header);
        if (string.Equals(requested, ILocaleResolver.All, StringComparison.OrdinalIgnoreCase))
        {
            return ILocaleResolver.All;
        }

        var locales = await registry.GetAsync(request.Site, cancellationToken);
        return requested is null ? locales.Default : locales.Validate(requested);
    }

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
