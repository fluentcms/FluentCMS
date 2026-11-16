namespace FluentCMS.Admin.Components;

/// <summary>Shows short, transient messages.</summary>
public interface ICmsToastService
{
    /// <summary>Shows a toast.</summary>
    /// <param name="message">The text of the toast.</param>
    /// <param name="intent">The kind of the toast.</param>
    /// <returns>A task that completes when the toast is shown.</returns>
    Task ShowAsync(string message, CmsToastIntent intent = CmsToastIntent.Info);
}
