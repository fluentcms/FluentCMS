namespace FluentCMS.Services;

public class AppException : ApplicationException
{
    public AppExceptionType Type { get; private set; }

    public AppException(AppExceptionType type, string message, int code = 0)
        : base(message)
    {
        Type = type;
        HResult = code;
    }

    public static AppException Throw(AppExceptionType type, string message, int code = 0) =>
        throw new AppException(AppExceptionType.BadRequest, message, code);

    public static AppException BadRequest(string message, int code = 400) =>
        new AppException(AppExceptionType.BadRequest, message, code);

    public static AppException Forbidden(string message, int code = 403) =>
        new AppException(AppExceptionType.Forbidden, message, code);
}

public enum AppExceptionType
{
    BadRequest = 400,
    Forbidden = 403,
}
