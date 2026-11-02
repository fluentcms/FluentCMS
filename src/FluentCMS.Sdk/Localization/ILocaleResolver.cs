namespace FluentCMS.Localization;

/// <summary>Decides which locale a request reads or writes.</summary>
public interface ILocaleResolver
{
    /// <summary>The locale that asks for every locale of an entry.</summary>
    const string All = "all";

    /// <summary>Resolves the locale. The order is the <c>locale</c> parameter, the <c>X-Cms-Locale</c> header, and the default locale of the site. <c>Accept-Language</c> plays no part.</summary>
    /// <param name="request">What the request says about its locale.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A locale code as the site lists it, or <see cref="All"/>.</returns>
    /// <exception cref="Content.ValidationException">The locale is not a well-formed BCP 47 tag, or the site does not list it.</exception>
    ValueTask<string> ResolveAsync(LocaleRequest request, CancellationToken cancellationToken = default);
}
