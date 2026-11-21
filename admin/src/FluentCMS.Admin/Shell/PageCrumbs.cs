namespace FluentCMS.Admin.Shell;

// The name a page gives the last item of its breadcrumbs, such as a user's once the page has read it, for the path the
// page is at, so that the name of one page is never shown on another. PageCrumb sets it.
internal sealed class PageCrumbs
{
    private (string Path, string Text)? _named;

    public event Action? Changed;

    // The name given for a path relative to the app's base, without its query string, if any.
    public string? For(string path) => _named is { } named && string.Equals(named.Path, path, StringComparison.OrdinalIgnoreCase) ? named.Text : null;

    public void Name(string path, string text)
    {
        if (_named != (path, text))
        {
            _named = (path, text);
            Changed?.Invoke();
        }
    }

    public void Clear(string path)
    {
        if (_named?.Path == path)
        {
            _named = null;
            Changed?.Invoke();
        }
    }
}
