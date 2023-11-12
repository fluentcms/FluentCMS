namespace FluentCMS.Services.Exceptions;

public class AppException : ApplicationException
{
    public Error ErrorCode { get; private set; }

    public AppException(Error error) : base(error.Message)
    {
        ErrorCode = error;
    }
}
