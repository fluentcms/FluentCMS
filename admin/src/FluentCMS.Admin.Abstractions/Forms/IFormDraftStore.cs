namespace FluentCMS.Admin.Forms;

/// <summary>
/// Keeps the unsaved forms of the tab across the end of a session. When the session ends, every tracked form is written to
/// the tab's session storage under the signed-in user before the admin redirects to sign in; after sign-in, the form finds
/// its draft, saves it with the draft's base version and removes it.
/// </summary>
public interface IFormDraftStore
{
    /// <summary>Gets whether a form of the tab is tracked, so has changes that are not saved, such as before a sign-out.</summary>
    bool HasUnsavedForms { get; }

    /// <summary>Tracks a form while it has unsaved changes.</summary>
    /// <param name="form">The form, asked for its draft when the session ends.</param>
    /// <returns>A handle that stops the tracking when disposed: once the form is saved, discarded or closed.</returns>
    IDisposable Track(IUnsavedForm form);

    /// <summary>Finds the signed-in user's draft of a form, whatever version it was based on.</summary>
    /// <param name="key">What the form edits.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The draft, or null when the user has none for the key or nobody is signed in.</returns>
    Task<FormDraft?> FindAsync(FormDraftKey key, CancellationToken cancellationToken);

    /// <summary>Removes the signed-in user's draft of a form, once it is saved or discarded.</summary>
    /// <param name="key">What the form edits.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the draft is removed.</returns>
    Task RemoveAsync(FormDraftKey key, CancellationToken cancellationToken);
}
