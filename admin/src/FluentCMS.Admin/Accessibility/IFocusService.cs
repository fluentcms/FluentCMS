namespace FluentCMS.Admin.Accessibility;

// Returns focus to the control that opened a dialog or a drawer of the admin's own. Remember the opener before the dialog takes
// focus and dispose the handle once it has closed: focus then moves back to the opener, if it is still on the page. The dialogs
// of the wrapped set return focus themselves.
internal interface IFocusService
{
    ValueTask<IAsyncDisposable> RememberOpenerAsync();
}
