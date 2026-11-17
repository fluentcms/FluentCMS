using System.Text.Json;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Interop;

namespace FluentCMS.Admin.Forms;

// The unsaved forms of the tab and their drafts in sessionStorage (row 3.3). All drafts are one item, a list of drafts each
// with its user, site, type, entry, locale and base version, so that one removal clears them at sign-out. A draft is found
// by its user, site, type, entry and locale, whatever its base version: after sign-in the form reads the entry again, maybe
// at a newer version, and saves the draft with the version it was based on, so a change made meanwhile is a 412.
internal sealed class FormDrafts(ISessionStorage storage)
{
    public const string StorageKey = "fluentcms-admin/drafts";

    private readonly Lock _gate = new();
    private readonly List<IUnsavedForm> _forms = [];

    public bool HasTracked
    {
        get
        {
            lock (_gate)
            {
                return _forms.Count > 0;
            }
        }
    }

    // Raised when the tab gets its first unsaved form and when it has none left, for the question before the tab closes.
    public event Action? TrackedChanged;

    public IDisposable Track(IUnsavedForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        bool first;
        lock (_gate)
        {
            _forms.Add(form);
            first = _forms.Count == 1;
        }

        if (first)
        {
            TrackedChanged?.Invoke();
        }

        return new Tracking(this, form);
    }

    // The user chose to leave the unsaved forms, as at a confirmed site switch or sign-out, or they are kept as drafts:
    // nothing asks about them any more.
    public void ReleaseAll()
    {
        bool had;
        lock (_gate)
        {
            had = _forms.Count > 0;
            _forms.Clear();
        }

        if (had)
        {
            TrackedChanged?.Invoke();
        }
    }

    // Before the redirect to sign in, when the session has ended.
    public async Task SaveTrackedAsync(string user)
    {
        List<FormDraft> drafts;
        lock (_gate)
        {
            drafts = [.. _forms.Select(form => form.CaptureDraft())];
        }

        if (drafts.Count == 0)
        {
            return;
        }

        var stored = await ReadAsync();
        foreach (var draft in drafts)
        {
            stored.RemoveAll(existing => Matches(existing, user, draft.Key));
            var (site, type, entry, locale) = draft.Key;
            stored.Add(new StoredDraft(user, site, type, entry, locale, draft.BaseVersion, draft.Fields.DeepClone().AsObject()));
        }

        // Kept, so the redirect to sign in does not ask; a write that failed leaves the question.
        await WriteAsync(stored);
        ReleaseAll();
    }

    public async Task<FormDraft?> FindAsync(string user, FormDraftKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return (await ReadAsync()).LastOrDefault(draft => Matches(draft, user, key)) is { } found
            ? new FormDraft(key, found.BaseVersion, found.Fields)
            : null;
    }

    public async Task RemoveAsync(string user, FormDraftKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var stored = await ReadAsync();
        if (stored.RemoveAll(draft => Matches(draft, user, key)) > 0)
        {
            await WriteAsync(stored);
        }
    }

    // When another user signs in to the tab, the drafts of the one before are of no use to it and are not kept for it.
    public async Task KeepOnlyAsync(string user)
    {
        var stored = await ReadAsync();
        if (stored.RemoveAll(draft => draft.User != user) > 0)
        {
            await WriteAsync(stored);
        }
    }

    // At sign-out, which the user confirmed when a form was unsaved.
    public ValueTask ClearAsync()
    {
        ReleaseAll();
        return storage.RemoveAsync(StorageKey);
    }

    private static bool Matches(StoredDraft draft, string user, FormDraftKey key) =>
        draft.User == user && draft.Site == key.Site && draft.Type == key.Type && draft.Entry == key.Entry && draft.Locale == key.Locale;

    // A value that does not read, written by another build, is dropped rather than failing the form.
    private async Task<List<StoredDraft>> ReadAsync()
    {
        if (await storage.GetAsync(StorageKey) is not { Length: > 0 } json)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, AuthenticationJsonContext.Default.ListStoredDraft) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAsync(List<StoredDraft> drafts)
    {
        if (drafts.Count == 0)
        {
            await storage.RemoveAsync(StorageKey);
            return;
        }

        await storage.SetAsync(StorageKey, JsonSerializer.Serialize(drafts, AuthenticationJsonContext.Default.ListStoredDraft));
    }

    private void Untrack(IUnsavedForm form)
    {
        bool last;
        lock (_gate)
        {
            last = _forms.Remove(form) && _forms.Count == 0;
        }

        if (last)
        {
            TrackedChanged?.Invoke();
        }
    }

    private sealed class Tracking(FormDrafts drafts, IUnsavedForm form) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                drafts.Untrack(form);
            }
        }
    }
}
