using FluentCMS.Services.ErrorModels;

namespace FluentCMS.Services;

public static class Errors
{
    // common errors
    public static ErrorCode BadRequest => new(ErrorType.BadRequest, "Common", 1, nameof(BadRequest));
    public static ErrorCode Permission => new(ErrorType.Forbidden, "Common", 1, "You don't have enough permission to do the operation");
    public static ErrorCode UserIsNotAuthenticated => new(ErrorType.Forbidden, "Common", 2, "User not authenticated.");

    public static class Users
    {
        public static ErrorCode UserDoesNotExists => new(ErrorType.BadRequest, nameof(Users), 1, nameof(UserDoesNotExists));
        public static ErrorCode DuplicateUserFound => new(ErrorType.BadRequest, nameof(Users), 2, nameof(UserDoesNotExists));
    }

    public static class Hosts
    {
        public static ErrorCode HostDoesNotExists => new(ErrorType.BadRequest, nameof(Hosts), 1, nameof(HostDoesNotExists));
        public static ErrorCode DuplicateHostFound => new(ErrorType.BadRequest, nameof(Hosts), 2, nameof(DuplicateHostFound));
        public static ErrorCode HostShouldHaveAtLeastOneSuperUser => new(ErrorType.BadRequest, nameof(Hosts), 3, "Host should have at least one super user");

        public static ErrorCode CantRemoveYourselfFromSuperUsers => new(ErrorType.Forbidden, nameof(Hosts), 1, "You can't remove yourself from super user list");
    }

    public static class Sites
    {
        public static ErrorCode SiteDoesNotExists => new(ErrorType.BadRequest, nameof(Sites), 1, nameof(SiteDoesNotExists));
        public static ErrorCode DuplicateSiteFound => new(ErrorType.BadRequest, nameof(Sites), 2, nameof(DuplicateSiteFound));
        public static ErrorCode DuplicateSiteUrlFound => new(ErrorType.BadRequest, nameof(Sites), 3, nameof(DuplicateSiteUrlFound));

        public static ErrorCode AccessDeniedToTheSite => new(ErrorType.Forbidden, nameof(Sites), 1, nameof(AccessDeniedToTheSite));
        public static ErrorCode OnlySuperUsersCanCreateOrUpdateOrDeleteSites => new(ErrorType.Forbidden, nameof(Sites), 2, nameof(OnlySuperUsersCanCreateOrUpdateOrDeleteSites));
    }
}
