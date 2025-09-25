namespace FluentCMS.Exceptions;

[Serializable]
public class EnhancedException : Exception
{
    public string? TypeName { get; set; }
    public string? MethodName { get; set; }
    public List<ErrorInfo> Errors { get; } = [];

    public EnhancedException(string code, string message) : base($"Code: {code}, Message: {message}")
    {
        Errors.Add(new ErrorInfo(code, message));
        CaptureExceptionSource();
    }

    public EnhancedException(string code, string message, Exception? innerException) : base($"Code: {code}, Message: {message}", innerException)
    {
        Errors.Add(new ErrorInfo(code, message));
        CaptureExceptionSource();
    }

    public EnhancedException(IEnumerable<string> codes) : base(string.Empty)
    {
        Errors.AddRange(codes.Select(c => new ErrorInfo(c)));
        CaptureExceptionSource();
    }

    public EnhancedException(IEnumerable<string> codes, Exception? innerException) : base(string.Empty, innerException)
    {
        Errors.AddRange(codes.Select(c => new ErrorInfo(c)));
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

    /// <summary>
    /// Add a single error to the exception
    /// </summary>
    public EnhancedException AddError(string code, string description)
    {
        Errors.Add(new ErrorInfo(code, description));
        return this;
    }

    /// <summary>
    /// Add multiple errors to the exception
    /// </summary>
    public EnhancedException AddErrors(IEnumerable<ErrorInfo> errors)
    {
        Errors.AddRange(errors);
        return this;
    }

    /// <summary>
    /// Creates a comprehensive string representation of the exception.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();

        // Header with basic information
        sb.AppendLine($"=== {TypeName} ===");
        sb.AppendLine($"Method: {MethodName}");
        sb.AppendLine($"Message: {Message}");

        // Display validation errors if present

        sb.AppendLine("\n=== Validation Errors ===");
        foreach (var error in Errors)
        {
            sb.AppendLine(error.ToString());
        }

        // Inner exception details
        if (InnerException != null)
        {
            sb.AppendLine("\n=== Inner Exception ===");
            sb.AppendLine(InnerException.ToString());
        }

        // Add stack trace
        sb.AppendLine("\n=== Stack Trace ===");
        sb.AppendLine(StackTrace);

        return sb.ToString();
    }

    ///// <summary>
    ///// Factory method to create an ArgumentException with enhanced information.
    ///// </summary>
    //public static EnhancedException ArgumentException(
    //    string paramName,
    //    object? invalidValue,
    //    string message,
    //    [CallerMemberName] string memberName = "",
    //    [CallerFilePath] string filePath = "",
    //    [CallerLineNumber] int lineNumber = 0)
    //{
    //    return new EnhancedException(
    //        $"Invalid argument '{paramName}' with value '{invalidValue}'. {message}",
    //        contextValue: paramName,
    //        memberName: memberName,
    //        filePath: filePath,
    //        lineNumber: lineNumber,
    //        innerException: null);
    //}

    ///// <summary>
    ///// Factory method for validation failures.
    ///// </summary>
    //public static EnhancedException ValidationFailed(
    //    bool condition,
    //    string message,
    //    [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null,
    //    [CallerMemberName] string memberName = "",
    //    [CallerFilePath] string filePath = "",
    //    [CallerLineNumber] int lineNumber = 0)
    //{
    //    if (condition)
    //    {
    //        throw new InvalidOperationException("ValidationFailed should only be called with a failing condition");
    //    }

    //    return new EnhancedException(
    //        $"Validation failed: {message}",
    //        contextValue: condition,
    //        contextExpression: conditionExpression,
    //        memberName: memberName,
    //        filePath: filePath,
    //        lineNumber: lineNumber,
    //        innerException: null);
    //}

    ///// <summary>
    ///// Factory method to create exception from ModelState validation errors
    ///// </summary>
    //public static EnhancedException FromModelState(
    //    IDictionary<string, ICollection<string>> modelStateErrors,
    //    string message = "Model validation failed",
    //    [CallerMemberName] string memberName = "",
    //    [CallerFilePath] string filePath = "",
    //    [CallerLineNumber] int lineNumber = 0)
    //{
    //    var exception = new EnhancedException(
    //        message,
    //        memberName: memberName,
    //        filePath: filePath,
    //        lineNumber: lineNumber);

    //    foreach (var kvp in modelStateErrors)
    //    {
    //        string propertyName = kvp.Key;
    //        foreach (var errorMessage in kvp.Value)
    //        {
    //            exception.AddError("ValidationError", errorMessage, propertyName);
    //        }
    //    }

    //    return exception;
    //}
}

/// <summary>
/// Represents a single validation error or message
/// </summary>
public class ErrorInfo(string code, string description)
{
    public ErrorInfo(string code) : this(code, string.Empty)
    {
    }

    public string Code { get; } = code ??
        throw new ArgumentNullException(nameof(code));

    public string Description { get; } = description ??
        throw new ArgumentNullException(nameof(description));

    public override string ToString() =>
        $"[{Code}] : {Description}";
}