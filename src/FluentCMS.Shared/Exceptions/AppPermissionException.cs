namespace FluentCMS;

public class AppPermissionException : AppException
{
    public AppPermissionException() : base(ExceptionCodes.GeneralPermissionDenied)
    {
    }
}
