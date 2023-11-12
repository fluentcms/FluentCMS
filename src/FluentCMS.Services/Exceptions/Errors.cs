namespace FluentCMS.Services.Exceptions;

public static class Errors
{
    // common errors
    public static Error BadRequest => new(ErrorArea.Common, ErrorType.BadRequest, 1, nameof(BadRequest));
    public static Error Permission => new(ErrorArea.Common, ErrorType.Forbidden, 1, "You don't have enough permission to do the operation");
    public static Error UserIsNotAuthenticated => new(ErrorArea.Common, ErrorType.Forbidden, 2, "User not authenticated.");

    public static class Users
    {
        public static Error UserDoesNotExists => new(ErrorArea.Users, ErrorType.BadRequest, 1, nameof(UserDoesNotExists));
        public static Error DuplicateUserFound => new(ErrorArea.Users, ErrorType.BadRequest, 2, nameof(UserDoesNotExists));
        public static Error AuthenticationFailed => new(ErrorArea.Users, ErrorType.BadRequest, 3, "User doesn't exist or username/password is not valid!");
    }
}
