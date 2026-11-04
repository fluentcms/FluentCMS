using FluentCMS.Sites;

namespace FluentCMS.Settings;

/// <summary>
/// Reads and writes the settings of one area. A read gives the settings of the site if it has any, otherwise the instance
/// defaults if there are any, otherwise a new instance of the class, whose property values are the code defaults. The
/// choice is of a whole document and never of single properties. Reads are cached for a short time and are not checked
/// against permissions, because they are for code that runs in the host; whatever shows settings to a person must check
/// <c>settings.{area}.manage</c> first.
/// </summary>
/// <typeparam name="T">The settings class, marked with <see cref="SettingsAttribute"/> and added with <c>cms.Settings.Add</c>.</typeparam>
public interface ISettings<T>
    where T : class
{
    /// <summary>Gets the settings that apply to a site.</summary>
    /// <param name="site">The site.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A new instance, which the caller may change without affecting anyone else.</returns>
    /// <exception cref="Content.ValidationException">There is no such site.</exception>
    ValueTask<T> GetAsync(SiteId site, CancellationToken cancellationToken = default);

    /// <summary>Gets the instance defaults, which apply to every site that has no settings of its own.</summary>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A new instance; the code defaults when no instance default was set.</returns>
    ValueTask<T> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the settings of a site, which then no longer follow the instance defaults. The write is the whole document, it is
    /// atomic, and when two writes meet the last one stands. Raises <c>SettingsChangedEvent</c> and records an activity row.
    /// </summary>
    /// <param name="site">The site.</param>
    /// <param name="value">The new settings.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the settings are stored and readers have been told.</returns>
    /// <exception cref="SettingsAccessDeniedException">The principal of the operation has no <c>settings.{area}.manage</c> for the site.</exception>
    /// <exception cref="Content.ValidationException">There is no such site, or the value breaks a validation attribute of the class.</exception>
    /// <exception cref="InvalidOperationException">There is no operation, or the caller is inside a transaction that it owns.</exception>
    ValueTask SetAsync(SiteId site, T value, CancellationToken cancellationToken = default);

    /// <summary>Replaces the instance defaults. It needs <c>settings.{area}.manage</c> from a grant with no site limit.</summary>
    /// <param name="value">The new defaults.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the defaults are stored and readers have been told.</returns>
    /// <exception cref="SettingsAccessDeniedException">The principal holds the permission only for some sites, or not at all.</exception>
    /// <exception cref="Content.ValidationException">The value breaks a validation attribute of the class.</exception>
    /// <exception cref="InvalidOperationException">There is no operation, or the caller is inside a transaction that it owns.</exception>
    ValueTask SetDefaultAsync(T value, CancellationToken cancellationToken = default);

    /// <summary>Removes the settings of a site, so that it follows the instance defaults again. Nothing happens, and no event is raised, when the site had none.</summary>
    /// <param name="site">The site.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the settings are removed and readers have been told.</returns>
    /// <exception cref="SettingsAccessDeniedException">The principal of the operation has no <c>settings.{area}.manage</c> for the site.</exception>
    /// <exception cref="Content.ValidationException">There is no such site.</exception>
    ValueTask ResetAsync(SiteId site, CancellationToken cancellationToken = default);

    /// <summary>Removes the instance defaults, so that sites without settings of their own get the code defaults again. It needs the same grant as <see cref="SetDefaultAsync"/>.</summary>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the defaults are removed and readers have been told.</returns>
    /// <exception cref="SettingsAccessDeniedException">The principal holds the permission only for some sites, or not at all.</exception>
    ValueTask ResetDefaultAsync(CancellationToken cancellationToken = default);
}
