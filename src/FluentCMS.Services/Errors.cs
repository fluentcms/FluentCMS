using FluentCMS.Services.ErrorModels;

namespace FluentCMS.Services;

public static class Errors
{
    // common errors
    public static ErrorCode BadRequest => new(ErrorType.BadRequest, "Common", 1, nameof(BadRequest));
    public static ErrorCode Permission => new(ErrorType.Forbidden, "Common", 1, nameof(Permission));

    public static class Users
    {
        public static ErrorCode UserDoesNotExists => new(ErrorType.BadRequest, nameof(Users), 1, nameof(UserDoesNotExists));
        public static ErrorCode DuplicateUserFound => new(ErrorType.BadRequest, nameof(Users), 2, nameof(UserDoesNotExists));
    }
}
