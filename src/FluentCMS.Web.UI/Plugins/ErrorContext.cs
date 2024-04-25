namespace FluentCMS.Web.UI.Plugins;

public class ErrorContext
{
    public string? LastError { get; set; } = null;
    public event EventHandler ErrorChanged;
    private static string GetError(Exception ex)
    {
        if (ex is ApiClientException apiClientException
            && apiClientException is not null
            && apiClientException.Data is not null
            && apiClientException.Data.Errors is var errors
            && errors is not null
            && errors.Any())
        {
            return string.Join("\n", errors.Select(x => string.IsNullOrEmpty(x.Description) ? x.Code : x.Description));
        }
        else
        {
            return ex.Message;
        }
    }

    public void SetError(Exception ex)
    {
        LastError = GetError(ex);
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Clear()
    {
        LastError = null;
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }
}
