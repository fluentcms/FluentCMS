namespace FluentCMS.Admin.Accessibility;

// A route change moves focus to the page's heading (spec 4.18), unless the page asked, just before it navigated, to keep focus
// where it is. The request is taken by the next navigation only, so going back to that route later focuses the heading.
internal sealed class NavigationFocus
{
    private bool _keep;

    public void KeepNext() => _keep = true;

    public bool TakeKeep()
    {
        var keep = _keep;
        _keep = false;
        return keep;
    }
}
