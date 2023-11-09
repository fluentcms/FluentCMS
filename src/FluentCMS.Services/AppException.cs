using FluentCMS.Services.ErrorModels;

namespace FluentCMS.Services;

public class AppException : ApplicationException
{
    public ErrorCode ErrorCode { get; private set; }

    public AppException(ErrorCode error) : base(error.Message)
    {
        ErrorCode = error;
    }
}
