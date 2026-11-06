using System.Collections.Frozen;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Seeding;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Publishing;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Every FCMS code the engine raises, once, with the status a client sees and a title. A code that is raised only while the
// host starts or a job runs is 500, since a request never reaches it. An unlisted code, such as a plugin's, is 422.
public static class ErrorCodeRegistry
{
    public static IReadOnlyList<ErrorCodeInfo> All { get; } =
    [
        // 11xx host, found when the host starts
        Startup(HostErrorCodes.InvalidOption, "Invalid option"),
        Startup(HostErrorCodes.GeneratedSigningKeyInProduction, "Generated signing key in Production"),
        Startup(HostErrorCodes.NotRegistered, "FluentCMS not registered"),
        Startup(HostErrorCodes.PipelineMissing, "FluentCMS pipeline missing"),

        // 12xx schema, found when the host starts
        Startup(SchemaErrorCodes.DuplicateType, "Duplicate content type"),
        Startup(SchemaErrorCodes.DuplicateField, "Duplicate field"),
        Startup(SchemaErrorCodes.FieldTypeNotRegistered, "Field type not registered"),
        Startup(SchemaErrorCodes.RelationTargetMissing, "Relation target missing"),
        Startup(SchemaErrorCodes.SlugSourceMissing, "Slug source missing"),
        Startup(SchemaErrorCodes.ReservedName, "Reserved name"),
        Startup(SchemaErrorCodes.NestingTooDeep, "Nesting too deep"),
        Startup(SchemaErrorCodes.LocalizableOnUnlocalizedType, "Localizable field on an unlocalized type"),
        Startup(SchemaErrorCodes.DuplicateReverseRelation, "Duplicate reverse relation"),
        Startup(SchemaErrorCodes.ComponentOrBlockMissing, "Component or block missing"),
        Startup(SchemaErrorCodes.InvalidField, "Invalid field"),
        Startup(SchemaErrorCodes.InvalidCondition, "Invalid condition"),
        Startup(SchemaErrorCodes.InvalidSchemaFile, "Invalid schema file"),
        Startup(SchemaErrorCodes.InvalidName, "Invalid name"),
        Startup(SchemaErrorCodes.InvalidType, "Invalid content type"),
        Startup(SchemaErrorCodes.InvalidProperty, "Invalid property"),
        Startup(SchemaErrorCodes.ReverseRelationOutsideContentType, "Reverse relation outside a content type"),

        // 13xx storage
        Startup(StorageErrorCodes.MissingContentSchema, "Content tables out of date"),
        Startup(StorageErrorCodes.NoDataProvider, "No data provider"),
        Startup(StorageErrorCodes.SeveralDataProviders, "Several data providers"),
        Startup(StorageErrorCodes.InvalidDatabaseSetting, "Invalid database setting"),
        Startup(StorageErrorCodes.PendingStreamMigrations, "Pending stream migrations"),

        // 14xx authorization
        Startup(AuthorizationErrorCodes.DuplicatePermission, "Duplicate permission"),
        Startup(AuthorizationErrorCodes.ReservedPermission, "Reserved permission"),
        Startup(AuthorizationErrorCodes.UnsupportedRowRule, "Unsupported row rule"),
        Startup(AuthorizationErrorCodes.DuplicateRowRule, "Duplicate row rule"),
        Startup(AuthorizationErrorCodes.InvalidRowRule, "Invalid row rule"),
        Invalid(AuthorizationErrorCodes.FieldNotWritable, "Field not writable"),

        // 21xx validation
        Invalid(ApiErrorCodes.ValidationFailed, "Validation failed"),
        Invalid(ValidationErrorCodes.Required, "Value required"),
        Invalid(ValidationErrorCodes.MinLength, "Too short"),
        Invalid(ValidationErrorCodes.MaxLength, "Too long"),
        Invalid(ValidationErrorCodes.BelowMinimum, "Below the minimum"),
        Invalid(ValidationErrorCodes.AboveMaximum, "Above the maximum"),
        Invalid(ValidationErrorCodes.PatternMismatch, "Does not match the pattern"),
        Invalid(ValidationErrorCodes.NotAnAllowedOption, "Not an allowed option"),
        Invalid(ValidationErrorCodes.InvalidValue, "Invalid value"),
        Invalid(ValidationErrorCodes.Custom, "Validation rule failed"),
        Invalid(ValidationErrorCodes.RequiredByCondition, "Value required by a condition"),
        Invalid(ValidationErrorCodes.NotUnique, "Value not unique"),

        // 22xx query
        BadQuery(QueryErrorCodes.InvalidParameter, "Invalid query parameter"),
        BadQuery(QueryErrorCodes.UnknownField, "Unknown field"),
        BadQuery(QueryErrorCodes.OperatorNotAllowed, "Operator not allowed"),
        BadQuery(QueryErrorCodes.InvalidValue, "Invalid query value"),
        BadQuery(QueryErrorCodes.NotSortable, "Field not sortable"),
        BadQuery(QueryErrorCodes.InvalidCursor, "Invalid cursor"),
        BadQuery(QueryErrorCodes.LimitExceeded, "Query limit exceeded"),
        BadQuery(QueryErrorCodes.NotARelation, "Not a relation"),
        new(QueryErrorCodes.DraftNotAllowed, StatusCodes.Status403Forbidden, "Drafts not allowed"),

        // 23xx sites and locales
        new(SiteErrorCodes.SiteNotFound, StatusCodes.Status404NotFound, "Site not found"),
        new(SiteErrorCodes.DefaultSiteProtected, StatusCodes.Status409Conflict, "Default site protected"),
        new(SiteErrorCodes.SiteHasContent, StatusCodes.Status409Conflict, "Site has content"),
        Invalid(SiteErrorCodes.InvalidSite, "Invalid site"),
        Invalid(LocaleErrorCodes.InvalidLocale, "Invalid locale"),
        Invalid(LocaleErrorCodes.LocaleNotAllowed, "Locale not allowed"),

        // 24xx content and publishing
        new(ContentErrorCodes.SingletonExists, StatusCodes.Status409Conflict, "Singleton already exists"),
        new(ContentErrorCodes.NotPublished, StatusCodes.Status409Conflict, "Entry not published"),
        Invalid(PublishWarningCodes.UnpublishedReference, "Reference to an unpublished entry"),
        Invalid(PublishWarningCodes.UnavailableReference, "Reference to an unavailable entry"),
        new(ContentErrorCodes.DraftMovedByHook, StatusCodes.Status409Conflict, "Draft changed by a hook"),
        new(ContentErrorCodes.NotScheduled, StatusCodes.Status409Conflict, "Nothing scheduled"),
        Invalid(ContentErrorCodes.ScheduleNotInFuture, "Schedule not in the future"),
        Invalid(ContentErrorCodes.NonexistentLocalTime, "Local time does not exist"),
        new(ContentErrorCodes.ScheduledDraftChanged, StatusCodes.Status409Conflict, "Draft changed since it was scheduled"),

        // 25xx jobs
        Startup(JobErrorCodes.UnknownJobType, "Unknown job type"),
        Startup(JobErrorCodes.BadPayload, "Bad job payload"),
        Startup(JobErrorCodes.InvalidCron, "Invalid cron expression"),
        Startup(JobErrorCodes.UnregisteredJob, "Unregistered job"),
        Startup(JobErrorCodes.DuplicateJobType, "Duplicate job type"),
        Startup(JobErrorCodes.InvalidRunnerOptions, "Invalid job runner options"),
        Startup(JobErrorCodes.BadActor, "Bad job actor"),
        Startup(JobErrorCodes.AttemptTimedOut, "Job attempt timed out"),

        // 26xx events
        Startup(EventErrorCodes.DuplicateOutboxHandler, "Duplicate outbox handler"),
        Startup(EventErrorCodes.PublishInForeignTransaction, "Event published in a foreign transaction"),
        Startup(EventErrorCodes.InvalidOutboxOptions, "Invalid outbox options"),

        // 27xx activity
        Startup(ActivityErrorCodes.InvalidOptions, "Invalid activity options"),
        Startup(ActivityErrorCodes.DuplicateActivity, "Duplicate activity"),
        BadQuery(ActivityErrorCodes.InvalidPage, "Invalid page"),
        Startup(ActivityErrorCodes.WriteInTransaction, "Activity written in a transaction"),

        // 28xx settings
        Startup(SettingsErrorCodes.InvalidRegistration, "Invalid settings registration"),
        new(SettingsErrorCodes.UnknownArea, StatusCodes.Status404NotFound, "Unknown settings area"),
        Invalid(SettingsErrorCodes.InvalidJson, "Invalid settings document"),
        Invalid(SettingsErrorCodes.UnknownKey, "Unknown settings key"),
        Invalid(SettingsErrorCodes.InvalidValue, "Invalid settings value"),
        new(SettingsErrorCodes.SiteNotFound, StatusCodes.Status404NotFound, "Site not found"),
        Startup(SettingsErrorCodes.WriteInTransaction, "Settings written in a transaction"),
        new(SettingsErrorCodes.VersionConflict, StatusCodes.Status412PreconditionFailed, "Settings changed since they were read"),

        // 29xx seeding
        Startup(SeedingErrorCodes.LocaleWithoutSeeder, "Locale without a seeder"),
        Startup(SeedingErrorCodes.InvalidTimeout, "Invalid seeding timeout"),

        // 30xx identity
        Startup(IdentityErrorCodes.InvalidOptions, "Invalid user options"),
        Invalid(IdentityErrorCodes.InvalidEmail, "Invalid email address"),
        new(IdentityErrorCodes.EmailTaken, StatusCodes.Status409Conflict, "Email address taken"),
        Invalid(IdentityErrorCodes.UnknownRole, "Unknown role"),
        new(IdentityErrorCodes.InvalidInvitation, StatusCodes.Status404NotFound, "Invitation not valid"),
        Invalid(IdentityErrorCodes.InvitationExpired, "Invitation expired"),
        new(IdentityErrorCodes.DeleteNotSupported, StatusCodes.Status409Conflict, "Users are deactivated, not deleted"),
        Startup(IdentityErrorCodes.NoEmailSender, "No email sender"),
        Startup(IdentityErrorCodes.WriteInTransaction, "Users written in a transaction"),
        Invalid(ApiKeyErrorCodes.InvalidName, "Invalid API key name"),
        Invalid(ApiKeyErrorCodes.UnknownSite, "Unknown site"),
        Invalid(ApiKeyErrorCodes.UnknownType, "Unknown content type"),
        Invalid(ApiKeyErrorCodes.InvalidExpiry, "Invalid API key expiry"),
        Invalid(ApiKeyErrorCodes.InvalidRateLimit, "Invalid rate limit"),
        new(ApiKeyErrorCodes.NotFound, StatusCodes.Status404NotFound, "API key not found"),
        new(ApiKeyErrorCodes.InvalidKey, StatusCodes.Status401Unauthorized, "API key not valid"),
        new(ApiKeyErrorCodes.Expired, StatusCodes.Status401Unauthorized, "API key expired"),
        new(ApiKeyErrorCodes.Revoked, StatusCodes.Status401Unauthorized, "API key revoked"),
        new(ApiKeyErrorCodes.SeveralCredentials, StatusCodes.Status401Unauthorized, "Several credentials"),
        Invalid(ApiKeyErrorCodes.EmptyBinding, "Empty API key binding"),
        Startup(OidcErrorCodes.InvalidClients, "Invalid client registration"),
        Startup(OidcErrorCodes.SigningCertificateUnusable, "Signing certificate unusable"),
        new(RoleErrorCodes.RoleNotFound, StatusCodes.Status404NotFound, "Role not found"),
        Invalid(RoleErrorCodes.InvalidRole, "Invalid role"),
        Invalid(RoleErrorCodes.InvalidGrant, "Invalid grant"),
        new(RoleErrorCodes.RoleFixed, StatusCodes.Status409Conflict, "Role cannot be changed"),
        new(RoleErrorCodes.RoleInUse, StatusCodes.Status409Conflict, "Role in use"),
        new(RoleErrorCodes.RoleChanged, StatusCodes.Status409Conflict, "Role changed"),
        new(IdentityErrorCodes.UserNotFound, StatusCodes.Status404NotFound, "User not found"),
        new(IdentityErrorCodes.SessionNotFound, StatusCodes.Status404NotFound, "Session not found"),
        new(IdentityErrorCodes.InvitationNotFound, StatusCodes.Status404NotFound, "Invitation not found"),
        Invalid(IdentityErrorCodes.InvalidUser, "Invalid user"),
        new(IdentityErrorCodes.RoleAlreadyHeld, StatusCodes.Status409Conflict, "Role already held"),
        new(IdentityErrorCodes.RoleNotHeld, StatusCodes.Status404NotFound, "Role not held"),
        new(IdentityErrorCodes.TwoFactorEnabled, StatusCodes.Status409Conflict, "Two-factor sign-in is on"),
        new(IdentityErrorCodes.TwoFactorNotEnabled, StatusCodes.Status409Conflict, "Two-factor sign-in is off"),
        new(IdentityErrorCodes.NoAuthenticator, StatusCodes.Status409Conflict, "No authenticator enrolled"),
        Invalid(IdentityErrorCodes.InvalidTwoFactorCode, "Invalid two-factor code"),

        // Admin SSO: options found when the host starts, and refusals of a sign-in that the hosted pages log
        Startup(IdentityErrorCodes.InvalidSsoOptions, "Invalid single sign-on options"),
        Startup(IdentityErrorCodes.SsoRoleNotFound, "Single sign-on role not found"),
        Startup(IdentityErrorCodes.SsoNoEmail, "No email address from the provider"),
        Startup(IdentityErrorCodes.SsoEmailTaken, "Email address of an unlinked user"),
        Startup(IdentityErrorCodes.SsoProvisioningFailed, "Single sign-on user not created"),

        // 31xx HTTP
        new(ApiErrorCodes.BadRequest, StatusCodes.Status400BadRequest, "Bad request"),
        new(ApiErrorCodes.Unauthorized, StatusCodes.Status401Unauthorized, "Authentication required"),
        new(ApiErrorCodes.Forbidden, StatusCodes.Status403Forbidden, "Forbidden"),
        new(ApiErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not found"),
        new(ApiErrorCodes.VersionConflict, StatusCodes.Status412PreconditionFailed, "Version conflict"),
        new(ApiErrorCodes.Unavailable, StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
        new(ApiErrorCodes.InternalError, StatusCodes.Status500InternalServerError, "Internal error"),
        new(LimitErrorCodes.RateLimited, StatusCodes.Status429TooManyRequests, "Too many requests"),
        new(LimitErrorCodes.RequestTooLarge, StatusCodes.Status413PayloadTooLarge, "Request too large"),
        new(RequestErrorCodes.SiteOutsideBinding, StatusCodes.Status403Forbidden, "Site outside the key's binding"),
        BadQuery(RequestErrorCodes.SiteNotNamed, "Site not named"),
        BadQuery(DeliveryErrorCodes.FieldNotUnique, "Field not unique"),
        new(PreconditionErrorCodes.IfMatchRequired, StatusCodes.Status428PreconditionRequired, "If-Match required"),
        BadQuery(PreconditionErrorCodes.IfMatchInvalid, "Invalid If-Match"),
        new(JobErrorCodes.JobNotFound, StatusCodes.Status404NotFound, "Job not found"),
        new(JobErrorCodes.NotDeadLettered, StatusCodes.Status409Conflict, "Job not dead-lettered"),
        new(JobErrorCodes.NotCancellable, StatusCodes.Status409Conflict, "Job cannot be cancelled"),
        new(LocaleErrorCodes.LocaleNotFound, StatusCodes.Status404NotFound, "Locale not found"),
        new(LocaleErrorCodes.LocaleInUse, StatusCodes.Status409Conflict, "Locale in use"),
        new(LocaleErrorCodes.LocaleExists, StatusCodes.Status409Conflict, "Locale exists"),
    ];

    private static readonly FrozenDictionary<string, ErrorCodeInfo> byCode = All.ToFrozenDictionary(info => info.Code);

    public static ErrorCodeInfo? Find(string? code) => code is not null && byCode.TryGetValue(code, out var info) ? info : null;

    private static ErrorCodeInfo Startup(string code, string title) => new(code, StatusCodes.Status500InternalServerError, title);

    private static ErrorCodeInfo Invalid(string code, string title) => new(code, StatusCodes.Status422UnprocessableEntity, title);

    private static ErrorCodeInfo BadQuery(string code, string title) => new(code, StatusCodes.Status400BadRequest, title);
}
