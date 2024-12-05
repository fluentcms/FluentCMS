using System.Diagnostics;

namespace FluentCMS;

/// <summary>
/// Represents a custom application exception with additional context, such as the source method and type, and associated errors.
/// </summary>
public class AppException : ApplicationException
{
    /// <summary>
    /// Gets or sets the name of the type where the exception occurred.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// Gets or sets the name of the method where the exception occurred.
    /// </summary>
    public string? MethodName { get; set; }

    /// <summary>
    /// Gets the list of errors associated with this exception.
    /// </summary>
    public List<AppError> Errors { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AppException"/> class with the specified error code.
    /// </summary>
    /// <param name="code">The error code associated with this exception.</param>
    public AppException(string code) : base(code)
    {
        Errors.Add(new AppError(code));
        CaptureExceptionSource();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppException"/> class with the specified error code and inner exception.
    /// </summary>
    /// <param name="code">The error code associated with this exception.</param>
    /// <param name="innerException">The inner exception that caused this exception.</param>
    public AppException(string code, Exception? innerException) : base(code, innerException)
    {
        Errors.Add(new AppError(code));
        CaptureExceptionSource();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppException"/> class with a collection of error codes.
    /// </summary>
    /// <param name="codes">The error codes associated with this exception.</param>
    public AppException(IEnumerable<string> codes) : base(string.Empty)
    {
        Errors.AddRange(codes.Select(c => new AppError(c)));
        CaptureExceptionSource();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppException"/> class with a collection of error codes and an inner exception.
    /// </summary>
    /// <param name="codes">The error codes associated with this exception.</param>
    /// <param name="innerException">The inner exception that caused this exception.</param>
    public AppException(IEnumerable<string> codes, Exception? innerException) : base(string.Empty, innerException)
    {
        Errors.AddRange(codes.Select(c => new AppError(c)));
        CaptureExceptionSource();
    }

    /// <summary>
    /// Captures the source type and method where the exception occurred.
    /// </summary>
    private void CaptureExceptionSource()
    {
        var stackTrace = new StackTrace(this, true);
        var frame = stackTrace.GetFrames()?.FirstOrDefault();
        var method = frame?.GetMethod();
        TypeName = method?.ReflectedType?.FullName;
        MethodName = method?.Name;
    }
}
