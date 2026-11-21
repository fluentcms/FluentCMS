using FluentCMS.Admin.Api;

namespace FluentCMS.Admin;

/// <summary>
/// Who is signed in to the admin, the site the shell shows and the content locale, with what the user may do there. The API
/// client is already set to the site and the locale.
/// </summary>
public interface IAdminContext
{
    /// <summary>Gets the signed-in user, from <c>GET /me</c>, or <see langword="null"/> until it is read.</summary>
    MeResponse? User { get; }

    /// <summary>Gets the site the shell shows, one of <c>GET /me/sites</c>, or <see langword="null"/> while there is none.</summary>
    MySiteInfo? Site { get; }

    /// <summary>Gets the content locale of the views, or <see langword="null"/> while there is no site.</summary>
    string? ContentLocale { get; }

    /// <summary>Gets what the user may do in <see cref="Site"/>.</summary>
    IAdminAccess Access { get; }

    /// <summary>Raised when the user switches site, or the context has been read again.</summary>
    event Action? Changed;

    /// <summary>
    /// Reads the user's sites again, from <c>GET /me/sites</c>, after a change of a site or of its locales, so that the site
    /// switcher and the locale switcher follow without a reload. When the site the shell shows is gone, as after it was
    /// deleted, the shell moves to the default site, or the first, as a switch does. A read that fails changes nothing.
    /// </summary>
    /// <returns>A task that completes once the sites are read, and the access of the site the shell shows when it changed.</returns>
    Task ReadSitesAsync() => Task.CompletedTask;
}
