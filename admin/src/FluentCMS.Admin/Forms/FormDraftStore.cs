using FluentCMS.Admin.Authentication;

namespace FluentCMS.Admin.Forms;

// The store the forms use, over the drafts of the tab, for the signed-in user only.
internal sealed class FormDraftStore(FormDrafts drafts, IAdminSession session) : IFormDraftStore
{
    public bool HasUnsavedForms => drafts.HasTracked;

    public IDisposable Track(IUnsavedForm form) => drafts.Track(form);

    public async Task<FormDraft?> FindAsync(FormDraftKey key, CancellationToken cancellationToken) =>
        session.User is { } user ? await drafts.FindAsync(user.Subject, key) : null;

    public async Task RemoveAsync(FormDraftKey key, CancellationToken cancellationToken)
    {
        if (session.User is { } user)
        {
            await drafts.RemoveAsync(user.Subject, key);
        }
    }
}
