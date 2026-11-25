namespace FluentCMS.Admin.Components;

// Cascaded to the body and the actions of a dialog, not to the close button of its title. Each enabled wrapped control there
// carries autofocus, and the library focuses the first in the document as the dialog opens (querySelector after showModal),
// so that focus lands on the dialog's first control, as before the close button came first in the document.
internal sealed class CmsDialogFocus
{
    public static readonly CmsDialogFocus Instance = new();
}
