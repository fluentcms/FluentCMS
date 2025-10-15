//using System.Diagnostics;

//namespace FluentCMS.Infrastructure.Exceptions;

//public class EnhancedException : ApplicationException
//{
//    public string? TypeName { get; set; }
//    public string? MethodName { get; set; }
//    public List<AppError> Errors { get; } = [];

//    public EnhancedException(string code) : base(code)
//    {
//        Errors.Add(new AppError(code));
//        CaptureExceptionSource();
//    }

//    public EnhancedException(string code, Exception? innerException) : base(code, innerException)
//    {
//        Errors.Add(new AppError(code));
//        CaptureExceptionSource();
//    }

//    public EnhancedException(IEnumerable<string> codes) : base(string.Empty)
//    {
//        Errors.AddRange(codes.Select(c => new AppError(c)));
//        CaptureExceptionSource();
//    }

//    public EnhancedException(IEnumerable<string> codes, Exception? innerException) : base(string.Empty, innerException)
//    {
//        Errors.AddRange(codes.Select(c => new AppError(c)));
//        CaptureExceptionSource();
//    }

//    private void CaptureExceptionSource()
//    {
//        var stackTrace = new StackTrace(this, true);
//        var frame = stackTrace.GetFrames()?.FirstOrDefault();
//        var method = frame?.GetMethod();
//        TypeName = method?.ReflectedType?.FullName;
//        MethodName = method?.Name;
//    }
//}
