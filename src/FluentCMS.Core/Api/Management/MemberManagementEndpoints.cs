using System.ComponentModel;
using System.Text.Json.Serialization;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Identity.Oidc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The members and member groups of the identity group of spec 4.10. The routes read the request and the services decide
// who may do what: members.manage for the instance, and every grant of a group's role to bind it or to put a member in it.
internal static class MemberManagementEndpoints
{
    private static readonly string Prefix = FluentCmsEndpointGroups.PrefixOf(FluentCmsEndpointGroup.Management);

    private static readonly string[] TrueOrFalse = ["true", "false"];

    public static IServiceCollection AddManagementMembersApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var members = management.MapGroup("/members").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        members.MapGet("", ListMembersAsync)
            .Query([
                QueryParameter.Text("search", "Only the members whose display name or email address contains this, in any case."),
                QueryParameter.Id("group", "Only the members of this member group."),
                QueryParameter.OneOf("verified", TrueOrFalse, "Only the members whose address is verified, or only those whose address is not."),
                QueryParameter.OneOf("active", TrueOrFalse, "Only the members that are not deactivated, or only those that are."),
                .. ManagementQuery.Paging])
            .Produces<ItemPage<MemberInfo>>();
        members.MapPost("", CreateMemberAsync).Takes<MemberCreateRequest>().Produces<MemberInfo>(StatusCodes.Status201Created);
        members.MapGet("/{id}", GetMemberAsync).Produces<MemberInfo>();
        members.MapPatch("/{id}", UpdateMemberAsync).Takes<MemberUpdateRequest>().Produces<MemberInfo>();
        members.MapDelete("/{id}", DeleteMemberAsync).Produces(StatusCodes.Status204NoContent);
        members.MapPost("/{id}/deactivate", DeactivateMemberAsync).Produces<MemberInfo>();
        members.MapPost("/{id}/reactivate", ReactivateMemberAsync).Produces<MemberInfo>();
        members.MapGet("/{id}/sessions", SessionsAsync).Produces<ItemList<UserSession>>();
        members.MapDelete("/{id}/sessions/{sessionId}", RevokeSessionAsync).Produces(StatusCodes.Status204NoContent);
        members.MapGet("/{id}/logins", LoginsAsync).Produces<ItemList<MemberLogin>>();
        members.MapDelete("/{id}/logins/{provider}", RemoveLoginAsync).Produces(StatusCodes.Status204NoContent);
        members.MapPut("/{id}/groups/{groupId}", AddToGroupAsync).Produces<MemberInfo>();
        members.MapDelete("/{id}/groups/{groupId}", RemoveFromGroupAsync).Produces<MemberInfo>();

        var groups = management.MapGroup("/member-groups").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        groups.MapGet("", ListGroupsAsync).Produces<ItemList<MemberGroupInfo>>();
        groups.MapPost("", CreateGroupAsync).Takes<MemberGroupWriteRequest>().Produces<MemberGroupInfo>(StatusCodes.Status201Created);
        groups.MapGet("/{id}", GetGroupAsync).Produces<MemberGroupInfo>();
        groups.MapPut("/{id}", UpdateGroupAsync).Takes<MemberGroupWriteRequest>().Produces<MemberGroupInfo>();
        groups.MapDelete("/{id}", DeleteGroupAsync).Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ListMembersAsync(HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        var request = httpContext.Request;
        ManagementQuery.Only(request);
        var (page, pageSize) = ManagementQuery.Page(request);
        var filter = new MemberFilter(
            ManagementQuery.Text(request, "search"),
            ManagementQuery.Id(request, "group"),
            Flag(request, "verified"),
            Flag(request, "active"));
        var found = await members.ListAsync(filter, page, pageSize, httpContext.RequestAborted);
        return AdminJson.Page(found.Items, found.Total, found.Page, found.PageSize);
    }

    private static async Task<IResult> GetMemberAsync(string id, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await members.GetAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> CreateMemberAsync(HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "email", "displayName");
        var email = body.String("email", required: true);
        var displayName = body.String("displayName");
        body.ThrowIfInvalid();

        var created = await members.CreateAsync(email!, displayName, httpContext.RequestAborted);
        httpContext.Response.Headers.Location = $"{httpContext.Request.PathBase}{Prefix}/members/{created.Id:D}";
        return AdminJson.Ok(created, StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateMemberAsync(string id, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        var memberId = ManagementQuery.RouteId(id, nameof(id));
        var body = await BodyReader.ReadAsync(httpContext.Request, "displayName");
        var displayName = body.String("displayName");
        body.ThrowIfInvalid();

        // A key that is left out, or null, leaves the value as it is; an empty text clears it.
        return AdminJson.Ok(displayName is null
            ? await members.GetAsync(memberId, httpContext.RequestAborted)
            : await members.UpdateAsync(memberId, displayName, httpContext.RequestAborted));
    }

    private static async Task<IResult> DeleteMemberAsync(string id, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        await members.DeleteAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> DeactivateMemberAsync(string id, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await members.DeactivateAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> ReactivateMemberAsync(string id, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await members.ReactivateAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> SessionsAsync(string id, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await members.SessionsAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> RevokeSessionAsync(string id, string sessionId, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        var (member, session) = (ManagementQuery.RouteId(id, nameof(id)), ManagementQuery.RouteId(sessionId, nameof(sessionId)));
        await members.RevokeSessionAsync(member, session, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> LoginsAsync(string id, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await members.LoginsAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> RemoveLoginAsync(string id, string provider, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        await members.RemoveLoginAsync(ManagementQuery.RouteId(id, nameof(id)), provider, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> AddToGroupAsync(string id, string groupId, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        var (member, group) = (ManagementQuery.RouteId(id, nameof(id)), ManagementQuery.RouteId(groupId, nameof(groupId)));
        return AdminJson.Ok(await members.AddToGroupAsync(member, group, httpContext.RequestAborted));
    }

    private static async Task<IResult> RemoveFromGroupAsync(string id, string groupId, HttpContext httpContext, [FromServices] MemberAdministration members)
    {
        ManagementQuery.Only(httpContext.Request);
        var (member, group) = (ManagementQuery.RouteId(id, nameof(id)), ManagementQuery.RouteId(groupId, nameof(groupId)));
        return AdminJson.Ok(await members.RemoveFromGroupAsync(member, group, httpContext.RequestAborted));
    }

    private static async Task<IResult> ListGroupsAsync(HttpContext httpContext, [FromServices] MemberGroupService groups)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Items(await groups.ListAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> GetGroupAsync(string id, HttpContext httpContext, [FromServices] MemberGroupService groups)
    {
        ManagementQuery.Only(httpContext.Request);
        return AdminJson.Ok(await groups.GetAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted));
    }

    private static async Task<IResult> CreateGroupAsync(HttpContext httpContext, [FromServices] MemberGroupService groups)
    {
        ManagementQuery.Only(httpContext.Request);
        var created = await groups.CreateAsync(await GroupRequestAsync(httpContext), httpContext.RequestAborted);
        httpContext.Response.Headers.Location = $"{httpContext.Request.PathBase}{Prefix}/member-groups/{created.Id:D}";
        return AdminJson.Ok(created, StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateGroupAsync(string id, HttpContext httpContext, [FromServices] MemberGroupService groups)
    {
        ManagementQuery.Only(httpContext.Request);
        var groupId = ManagementQuery.RouteId(id, nameof(id));
        return AdminJson.Ok(await groups.UpdateAsync(groupId, await GroupRequestAsync(httpContext), httpContext.RequestAborted));
    }

    private static async Task<IResult> DeleteGroupAsync(string id, HttpContext httpContext, [FromServices] MemberGroupService groups)
    {
        ManagementQuery.Only(httpContext.Request);
        await groups.DeleteAsync(ManagementQuery.RouteId(id, nameof(id)), httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<MemberGroupRequest> GroupRequestAsync(HttpContext httpContext)
    {
        var body = await BodyReader.ReadAsync(httpContext.Request, "name", "description", "roleId");
        var name = body.String("name", required: true);
        var description = body.String("description");
        var roleId = body.Id("roleId", required: true);
        body.ThrowIfInvalid();
        return new MemberGroupRequest(name!, description, roleId!.Value);
    }

    private static bool? Flag(HttpRequest request, string name) => ManagementQuery.Text(request, name) switch
    {
        null => null,
        "true" => true,
        "false" => false,
        var text => throw new ValidationException([new ValidationError(name, QueryErrorCodes.InvalidValue, $"'{text}' is not true or false.")]),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MemberCreateRequest
{
    public required string Email { get; init; }

    public string? DisplayName { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MemberUpdateRequest
{
    [Description("Left out, the member keeps the display name it has; an empty text clears it.")]
    public string? DisplayName { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MemberGroupWriteRequest
{
    [Description("Lower-case letters, digits, '-' and '_'; a row rule reads it from the principal.")]
    public required string Name { get; init; }

    public string? Description { get; init; }

    [Description("The role the members of the group hold, of the instance or of a site.")]
    public required Guid RoleId { get; init; }
}
