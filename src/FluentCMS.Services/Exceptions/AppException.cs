namespace FluentCMS.Services.Exceptions;

public class AppException : ApplicationException
{
    public IEnumerable<AppError> Errors { get; private set; }

    public AppException(AppError error) : base(error.Code)
    {
        Errors = new[] { error };
    }

    public AppException(IEnumerable<AppError> errors) : base(errors.First().Code)
    {
        Errors = errors;
    }
}
