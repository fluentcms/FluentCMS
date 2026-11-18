using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin;

/// <summary>
/// Shows content over the page that no page's markup holds, such as the dialog of a picker that an admin half provides as a
/// service (<c>IMediaPicker</c>). The shell draws it after the page, in the admin's theme, until it is removed.
/// </summary>
public interface IAdminOverlays
{
    /// <summary>Shows content until the returned handle is disposed.</summary>
    /// <param name="content">The content, usually one component.</param>
    /// <returns>The handle that removes the content; disposing it again does nothing.</returns>
    /// <exception cref="InvalidOperationException">The shell draws no overlays, as before it has started.</exception>
    IDisposable Show(RenderFragment content);
}
