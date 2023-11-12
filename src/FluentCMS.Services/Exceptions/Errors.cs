namespace FluentCMS.Services.Exceptions;

public static class Errors
{
    // common errors
    public static AppError BadRequest => new(ErrorType.BadRequest, ErrorArea.Common, nameof(BadRequest));
    public static AppError MissingPermission => new(ErrorType.Forbidden, ErrorArea.Common, nameof(MissingPermission), "You don't have enough permission to do the operation");
    public static AppError UserIsNotAuthenticated => new(ErrorType.Forbidden, ErrorArea.Common, nameof(UserIsNotAuthenticated), "User not authenticated.");

    public static class Users
    {
        public static AppError UserDoesNotExists => new(ErrorType.BadRequest, ErrorArea.Users, nameof(UserDoesNotExists));
        public static AppError DuplicateUserFound => new(ErrorType.BadRequest, ErrorArea.Users, nameof(UserDoesNotExists));
        public static AppError AuthenticationFailed => new(ErrorType.BadRequest, ErrorArea.Users, nameof(AuthenticationFailed), "User doesn't exist or username/password is not valid!");
    }
}
