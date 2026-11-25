namespace FluentCMS.Admin.Administration.Account;

// Set while the once-shown recovery codes are open and while the sign-in that follows them is on its way. The verify has
// revoked the tab's access and refresh tokens by then, so any call of the page could find it refused and take the page, and the codes, away:
// the other cards of the account page are inert meanwhile.
internal sealed class AccountHold
{
    public bool Held { get; private set; }

    public event Action? Changed;

    public void Set(bool held)
    {
        if (Held != held)
        {
            Held = held;
            Changed?.Invoke();
        }
    }
}
