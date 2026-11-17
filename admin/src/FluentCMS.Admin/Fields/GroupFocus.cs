using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Fields;

// Whether focus has left a group of inputs, such as a date and a time or a list of checkboxes, rather than moved inside it.
// The browser raises focusout on the group's input before focusin on the next, and FocusEventArgs has no relatedTarget, so a
// focusout is taken as leaving only when no focusin follows before the next turn of the event loop.
internal sealed class GroupFocus
{
    private int generation;

    public void Entered() => generation++;

    public async Task LeftAsync(EventCallback onBlur)
    {
        var left = ++generation;
        await Task.Delay(1);
        if (left == generation)
        {
            await onBlur.InvokeAsync();
        }
    }
}
