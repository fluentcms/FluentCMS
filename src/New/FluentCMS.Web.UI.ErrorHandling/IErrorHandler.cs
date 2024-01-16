namespace FluentCMS.Web.UI.ErrorHandling;

public interface IErrorHandler
{
    public Task HandleException(Exception ex);
}

public interface IErrorHandler<T> : IErrorHandler
    where T : Exception
{
    public Task HandleException(T ex) => HandleException((Exception)ex);
}
