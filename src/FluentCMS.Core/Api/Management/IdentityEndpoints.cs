using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The identity group of spec 4.10: users and their sessions, invitations, roles, the permission catalogue and API keys. The
// routes read the request and the services decide who may do what (users.manage, roles.manage, apikeys.manage, and no
// grant above the caller's own reach).
internal static class IdentityEndpoints
{
    // The hosted page that accepts an invitation (#2779), which the link in the email opens.
    private const string AcceptInvitationPath = "/account/accept-invitation";

    private static readonly string Prefix = FluentCmsEndpointGroups.Prefixes.Single(item => item.Group == FluentCmsEndpointGroup.Management).Prefix;

    public static IServiceCollection AddManagementIdentityApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var users = management.MapGroup("/users").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        users.MapGet("", ListUsersAsync);
        users.MapGet("/{id}", GetUserAsync);
        users.MapPatch("/{id}", UpdateUserAsync);
        users.MapPost("/{id}/deactivate", DeactivateAsync);
        users.MapPost("/{id}/reactivate", ReactivateAsync);
        users.MapPut("/{id}/roles/{roleId}", AddRoleAsync);
        users.MapDelete("/{id}/roles/{roleId}", RemoveRoleAsync);
        users.MapGet("/{id}/sessions", SessionsAsync);
        users.MapDelete("/{id}/sessions/{sessionId}", RevokeSessionAsync);

        var invitations = management.MapGroup("/invitations").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        invitations.MapGet("", ListInvitationsAsync);
        invitations.MapPost("", InviteAsync);
        invitations.MapDelete("/{id}", RevokeInvitationAsync);

        var roles = management.MapGroup("/roles").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        roles.MapGet("", ListRolesAsync);
        roles.MapPost("", CreateRoleAsync);
        roles.MapGet("/{id}", GetRoleAsync);
        roles.MapPut("/{id}", UpdateRoleAsync);
        roles.MapDelete("/{id}", DeleteRoleAsync);

        management.MapGroup("/permissions").WithFluentCmsOperation(Origin.Api).RequireManagementScope().MapGet("", PermissionsAsync);

        var keys = management.MapGroup("/api-keys").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        keys.MapGet("", ListKeysAsync);
        keys.MapPost("", CreateKeyAsync);
        keys.MapGet("/{id}", GetKeyAsync);
        keys.MapDelete("/{id}", RevokeKeyAsync);
        keys.MapPost("/{id}/expire", ExpireKeyAsync);
    }

    private static async Task<IResult> ListUsersAsync(HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request, "search", "page", "pageSize");
        var (page, pageSize) = ManagementQuery.Page(httpContext.Request);
        var found = await users.ListAsync(ManagementQuery.Text(httpContext.Request, "search"), page, pageSize, httpContext.RequestAborted);
        return AdminJson.Page(found.Items, found.Total, found.Page, found.PageSize);
    }

    private static async Task<IResult> GetUserAsync(string id, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await users.GetAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> UpdateUserAsync(string id, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        var userId = ManagementQuery.RouteId(id, nameof(id));
        var body = await BodyReader.ReadAsync(httpContext.Request, "userName", "email", "mustChangePassword");
        var change = new UserChange { UserName = body.String("userName"), Email = body.String("email"), MustChangePassword = body.Boolean("mustChangePassword") };
        body.ThrowIfInvalid();
        return AdminJson.Ok(await users.UpdateAsync(userId, change, httpContext.RequestAborted));
    }

    private static async Task<IResult> DeactivateAsync(string id, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await users.DeactivateAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> ReactivateAsync(string id, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await users.ReactivateAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> AddRoleAsync(string id, string roleId, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        var (user, role) = (ManagementQuery.RouteId(id, nameof(id)), ManagementQuery.RouteId(roleId, nameof(roleId)));
        return AdminJson.Ok(await users.AddRoleAsync(user, role, httpContext.RequestAborted));
    }

    private static async Task<IResult> RemoveRoleAsync(string id, string roleId, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        var (user, role) = (ManagementQuery.RouteId(id, nameof(id)), ManagementQuery.RouteId(roleId, nameof(roleId)));
        return AdminJson.Ok(await users.RemoveRoleAsync(user, role, httpContext.RequestAborted));
    }

    private static async Task<IResult> SessionsAsync(string id, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await users.SessionsAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> RevokeSessionAsync(string id, string sessionId, HttpContext httpContext, [FromServices] UserAdministration users)
    {
        ManagementQuery.Only(httpContext.Request);
        var (user, session) = (ManagementQuery.RouteId(id, nameof(id)), ManagementQuery.RouteId(sessionId, nameof(sessionId)));
        await users.RevokeSessionAsync(user, session, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> ListInvitationsAsync(HttpContext httpContext, [FromServices] InvitationService invitations)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await invitations.ListPendingAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> InviteAsync(HttpContext httpContext, [FromServices] InvitationService invitations)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "email", "roleIds");
        var email = body.String("email", required: true);
        var roleIds = body.Ids("roleIds") ?? [];
        body.ThrowIfInvalid();

        var request = httpContext.Request;
        var accept = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}{AcceptInvitationPath}");
        var sent = await invitations.InviteAsync(email!, [.. roleIds], accept, httpContext.RequestAborted);
        return AdminJson.Ok(sent, StatusCodes.Status201Created);
    }

    private static async Task<IResult> RevokeInvitationAsync(string id, HttpContext httpContext, [FromServices] InvitationService invitations)
    {
        ManagementQuery.Only(httpContext.Request);
        var invitationId = ManagementQuery.RouteId(id, nameof(id));
        if (!await invitations.RevokeAsync(invitationId, httpContext.RequestAborted))
        {
            throw new ValidationException([new ValidationError("id", IdentityErrorCodes.InvitationNotFound, $"There is no pending invitation {invitationId:D}.")]);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ListRolesAsync(HttpContext httpContext, [FromServices] RoleService roles)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items((await roles.ListAsync(httpContext.RequestAborted)).Select(AdminJson.Role));
    }

    private static async Task<IResult> GetRoleAsync(string id, HttpContext httpContext, [FromServices] RoleService roles)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(AdminJson.Role(await roles.GetAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted)));
    }

    private static async Task<IResult> CreateRoleAsync(HttpContext httpContext, [FromServices] RoleService roles)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "name", "description", "siteId", "grants");
        var request = RoleRequest(body) with { SiteId = body.Id("siteId") };
        body.ThrowIfInvalid();

        var created = await roles.CreateAsync(request, httpContext.RequestAborted);
        httpContext.Response.Headers.Location = $"{httpContext.Request.PathBase}{Prefix}/roles/{created.Id:D}";
        return AdminJson.Ok(AdminJson.Role(created), StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateRoleAsync(string id, HttpContext httpContext, [FromServices] RoleService roles)
    {
        ManagementQuery.Only(httpContext.Request);
        var roleId = ManagementQuery.RouteId(id, nameof(id));
        var body = await BodyReader.ReadAsync(httpContext.Request, "name", "description", "grants");
        var request = RoleRequest(body);
        body.ThrowIfInvalid();
        return AdminJson.Ok(AdminJson.Role(await roles.UpdateAsync(roleId, request, httpContext.RequestAborted)));
    }

    private static async Task<IResult> DeleteRoleAsync(string id, HttpContext httpContext, [FromServices] RoleService roles)
    {
        ManagementQuery.Only(httpContext.Request);
        await roles.DeleteAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> PermissionsAsync(HttpContext httpContext, [FromServices] RoleService roles)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await roles.CatalogueAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> ListKeysAsync(HttpContext httpContext, [FromServices] ApiKeyService keys)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await keys.ListAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> GetKeyAsync(string id, HttpContext httpContext, [FromServices] ApiKeyService keys)
    {
        ManagementQuery.Only(httpContext.Request);
        var keyId = ManagementQuery.RouteId(id, nameof(id));
        return AdminJson.Ok(await keys.FindAsync(keyId, httpContext.RequestAborted) ?? throw KeyNotFound(keyId));
    }

    private static async Task<IResult> CreateKeyAsync(HttpContext httpContext, [FromServices] ApiKeyService keys)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "name", "class", "roleId", "siteIds", "typeNames", "expiresAt", "rateLimit");
        var name = body.String("name", required: true);
        var keyClass = ClassOf(body);
        var roleId = body.Id("roleId", required: true);
        var request = new ApiKeyRequest(name ?? "", keyClass ?? ApiKeyClass.Delivery, roleId ?? Guid.Empty)
        {
            SiteIds = body.Ids("siteIds"),
            TypeNames = body.Strings("typeNames"),
            ExpiresAt = body.Moment("expiresAt"),
            RateLimit = body.Integer("rateLimit"),
        };
        body.ThrowIfInvalid();

        var created = await keys.CreateAsync(request, httpContext.RequestAborted);
        httpContext.Response.Headers.Location = $"{httpContext.Request.PathBase}{Prefix}/api-keys/{created.Key.Id:D}";
        return AdminJson.Ok(AdminJson.CreatedKey(created), StatusCodes.Status201Created);
    }

    // A revoked key stays, so that the log and the list still name it.
    private static async Task<IResult> RevokeKeyAsync(string id, HttpContext httpContext, [FromServices] ApiKeyService keys)
    {
        ManagementQuery.Only(httpContext.Request);
        await keys.RevokeAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> ExpireKeyAsync(string id, HttpContext httpContext, [FromServices] ApiKeyService keys)
    {
        ManagementQuery.Only(httpContext.Request);
        var keyId = ManagementQuery.RouteId(id, nameof(id));
        var body = httpContext.Request.ContentLength is 0 or null && !httpContext.Request.HasJsonContentType()
            ? null
            : await BodyReader.ReadAsync(httpContext.Request, "at");
        var at = body?.Moment("at");
        body?.ThrowIfInvalid();

        await keys.ExpireAsync(keyId, at, httpContext.RequestAborted);
        return AdminJson.Ok(await keys.FindAsync(keyId, httpContext.RequestAborted) ?? throw KeyNotFound(keyId));
    }

    private static RoleRequest RoleRequest(BodyReader body)
    {
        var name = body.String("name", required: true);
        var description = body.String("description");
        List<PermissionGrant> grants = [];
        if (body.Element("grants", required: true) is { } array)
        {
            if (array.ValueKind != JsonValueKind.Array)
            {
                body.Error("grants", "'grants' is an array of { permission, constraints }.");
            }
            else
            {
                var index = 0;
                foreach (var item in array.EnumerateArray())
                {
                    if (Grant(item, $"grants[{index++}]", body) is { } grant)
                    {
                        grants.Add(grant);
                    }
                }
            }
        }

        return new RoleRequest(name ?? "", description, grants);
    }

    private static PermissionGrant? Grant(JsonElement item, string path, BodyReader body)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            body.Error(path, "A grant is an object with a permission and optional constraints.");
            return null;
        }

        var grant = new BodyReader(item, "permission", "constraints");
        var permission = grant.String("permission", required: true);
        var constraints = PermissionConstraints.None;
        if (grant.Element("constraints") is { ValueKind: not JsonValueKind.Null } value)
        {
            try
            {
                constraints = value.ValueKind == JsonValueKind.Object ? PermissionConstraints.FromJson(value.GetRawText()) : throw new JsonException();
            }
            catch (JsonException)
            {
                body.Error(path + ".constraints", "The constraints are an object of sites, locales, fields and rowRule, and nothing else.");
            }
        }

        try
        {
            grant.ThrowIfInvalid();
        }
        catch (ValidationException exception)
        {
            foreach (var error in exception.Errors)
            {
                body.Error($"{path}.{error.Path}", error.Message);
            }

            return null;
        }

        return new PermissionGrant(permission!, constraints);
    }

    private static ApiKeyClass? ClassOf(BodyReader body) => body.String("class", required: true) switch
    {
        null => null,
        "delivery" => ApiKeyClass.Delivery,
        "preview" => ApiKeyClass.Preview,
        "management" => ApiKeyClass.Management,
        _ => Invalid(body),
    };

    private static ApiKeyClass? Invalid(BodyReader body)
    {
        body.Error("class", "The class is delivery, preview or management.");
        return null;
    }

    private static ValidationException KeyNotFound(Guid id) =>
        new([new ValidationError("id", ApiKeyErrorCodes.NotFound, $"There is no API key {id:D}.")]);
}
