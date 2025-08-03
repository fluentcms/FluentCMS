using System.Diagnostics;

namespace FluentCMS;

public class AppException : ApplicationException
{
    public string? TypeName { get; set; }
    public string? MethodName { get; set; }
    public List<AppError> Errors { get; } = [];

    public AppException(string code) : base(code)
    {
        Errors.Add(new AppError(code));
        CaptureExceptionSource();
    }

    public AppException(string code, Exception? innerException) : base(code, innerException)
    {
        Errors.Add(new AppError(code));
        CaptureExceptionSource();
    }

    public AppException(IEnumerable<string> codes) : base(string.Empty)
    {
        Errors.AddRange(codes.Select(c => new AppError(c)));
        CaptureExceptionSource();
    }

    public AppException(IEnumerable<string> codes, Exception? innerException) : base(string.Empty, innerException)
    {
        Errors.AddRange(codes.Select(c => new AppError(c)));
        CaptureExceptionSource();
    }

    private void CaptureExceptionSource()
    {
        var stackTrace = new StackTrace(this, true);
        var frame = stackTrace.GetFrames()?.FirstOrDefault();
        var method = frame?.GetMethod();
        TypeName = method?.ReflectedType?.FullName;
        MethodName = method?.Name;
    }
}
