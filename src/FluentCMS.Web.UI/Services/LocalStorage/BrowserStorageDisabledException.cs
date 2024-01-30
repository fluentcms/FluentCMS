namespace FluentCMS.Web.UI.Services.LocalStorage;

public class BrowserStorageDisabledException : AppException
{
    public BrowserStorageDisabledException() : base("LocalStorageError")
    {
    }
    public BrowserStorageDisabledException(Exception inner) : base("LocalStorageError", inner)
    {
    }
}
