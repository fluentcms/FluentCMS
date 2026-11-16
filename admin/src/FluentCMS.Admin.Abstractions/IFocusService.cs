namespace FluentCMS.Admin;

/// <summary>
/// Returns focus to the control that opened a dialog or a drawer. Call <see cref="RememberOpenerAsync"/> before the dialog
/// takes focus and dispose the result once it has closed: focus then moves back to the opener, if it is still on the page.
/// </summary>
public interface IFocusService
{
    /// <summary>Remembers the element that has focus now, the opener.</summary>
    /// <returns>A handle whose disposal moves focus back to the opener.</returns>
    ValueTask<IAsyncDisposable> RememberOpenerAsync();
}
