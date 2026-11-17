namespace FluentCMS.Admin.Interop;

// The other admin tabs of the browser, which answer for the session each holds. A tab duplicated from another, or opened by
// it, starts with a copy of its sessionStorage and finds that the other still holds the session it copied.
internal interface ITabChannel
{
    ValueTask<bool> IsHeldElsewhereAsync(string tabId);

    ValueTask HoldAsync(string tabId);
}
