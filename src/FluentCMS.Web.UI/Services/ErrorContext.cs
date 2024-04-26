namespace FluentCMS.Web.UI.Services;

public class ErrorContext
{
    public List<string> Errors { get; set; } = new List<string>();
    public event EventHandler ErrorChanged;
    public void SetError(Exception ex)
    {
        if (ex is ApiClientException apiClientException
            && apiClientException is not null
            && apiClientException.Data is not null
            && apiClientException.Data.Errors is var errors
            && errors is not null
            && errors.Any())
        {
            foreach (var error in errors)
            {
                var message = new[] { error.Description, error.Code }.FirstOrDefault(x => !string.IsNullOrEmpty(x));
                if (!string.IsNullOrEmpty(message))
                {
                    Errors.Add(message);
                }
                else if (!Errors.Any())
                {
                    Errors.Add(apiClientException.Message);
                    break;
                }
            }
        }
        else
        {
            Errors.Add(ex.Message);
        }
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Clear()
    {
        Errors.Clear();
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }
}
