namespace FluentCMS.Admin.Shell;

// Whether a navigation waits for what its page needs before the page can be shown (#3446): the shell's first reads, the
// access of the content locale the address names, or the assembly of a half or of the administration pages, which the
// router's OnNavigateAsync waits for; or the access of a new content locale, for which the route view holds the gate of the
// page. The layout shows a loading state meanwhile, and the app draws the layout before the router's first page, which the
// router draws only once its first navigation has ended.
internal sealed class NavigationProgress
{
    private int _waits;
    private bool _held;

    public event Action? Changed;

    public bool Waiting => _waits > 0 || _held;

    // Whether the router has drawn a page in this page load.
    public bool PageShown { get; private set; }

    // A wait the navigation ends by disposing what this returns.
    public IDisposable Wait()
    {
        Update(() => _waits++);
        return new Ending(this);
    }

    public void Hold(bool held) => Update(() => _held = held);

    public void ShowPage()
    {
        if (!PageShown)
        {
            PageShown = true;
            Changed?.Invoke();
        }
    }

    private void Update(Action change)
    {
        var before = Waiting;
        change();
        if (Waiting != before)
        {
            Changed?.Invoke();
        }
    }

    private sealed class Ending(NavigationProgress progress) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (!_ended)
            {
                _ended = true;
                progress.Update(() => progress._waits--);
            }
        }
    }
}
