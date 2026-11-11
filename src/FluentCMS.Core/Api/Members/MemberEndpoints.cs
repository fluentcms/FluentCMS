using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Members;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Members;

// /api/members/v1 of spec 4.10: what a front end calls for a visitor's own account. Registering, verifying and resetting
// need no token; /me needs a member's. The routes read the request, and MemberAccountService decides: whether
// registration is open, what a token is worth, and that only a member reads /me and its external logins. The three
// routes that send mail answer 202 with no body whatever the address, so the answer never tells whether it is a member's.
internal static class MemberEndpoints
{
    public static IServiceCollection AddMembersApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Members, Map);

    private static void Map(RouteGroupBuilder group)
    {
        var members = group.MapGroup("").WithFluentCmsOperation(Origin.Api);
        members.MapPost("/register", RegisterAsync).Produces(StatusCodes.Status202Accepted);
        members.MapPost("/verify-email", VerifyEmailAsync).Produces(StatusCodes.Status204NoContent);
        members.MapPost("/resend-verification", ResendVerificationAsync).Produces(StatusCodes.Status202Accepted);
        members.MapPost("/forgot-password", ForgotPasswordAsync).Produces(StatusCodes.Status202Accepted);
        members.MapPost("/reset-password", ResetPasswordAsync).Produces(StatusCodes.Status204NoContent);
        members.MapGet("/me", MeAsync).Produces<MemberProfile>();
        members.MapPatch("/me", UpdateMeAsync).Produces<MemberProfile>();
        members.MapPost("/me/change-password", ChangePasswordAsync).Produces(StatusCodes.Status204NoContent);
        members.MapGet("/me/logins", MyLoginsAsync).Produces<ItemList<MemberLogin>>();
        members.MapDelete("/me/logins/{provider}", RemoveMyLoginAsync).Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> RegisterAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "email", "password", "displayName");
        var email = body.String("email", required: true);
        var password = body.String("password", required: true);
        var displayName = body.String("displayName");
        body.ThrowIfInvalid();

        await account.RegisterAsync(email!, password!, displayName, httpContext.RequestAborted);
        return Results.Accepted();
    }

    private static async Task<IResult> VerifyEmailAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "email", "token");
        var email = body.String("email", required: true);
        var token = body.String("token", required: true);
        body.ThrowIfInvalid();

        await account.VerifyEmailAsync(email!, token!, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> ResendVerificationAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        await account.ResendVerificationAsync(await EmailAsync(httpContext), httpContext.RequestAborted);
        return Results.Accepted();
    }

    private static async Task<IResult> ForgotPasswordAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        await account.ForgotPasswordAsync(await EmailAsync(httpContext), httpContext.RequestAborted);
        return Results.Accepted();
    }

    private static async Task<IResult> ResetPasswordAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "email", "token", "password");
        var email = body.String("email", required: true);
        var token = body.String("token", required: true);
        var password = body.String("password", required: true);
        body.ThrowIfInvalid();

        await account.ResetPasswordAsync(email!, token!, password!, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        return Private(httpContext, await account.MeAsync(httpContext.RequestAborted));
    }

    private static async Task<IResult> UpdateMeAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "displayName");
        var displayName = body.String("displayName");
        body.ThrowIfInvalid();

        // A key that is left out, or null, leaves the value as it is; an empty text clears it.
        return Private(httpContext, displayName is null
            ? await account.MeAsync(httpContext.RequestAborted)
            : await account.UpdateMeAsync(displayName, httpContext.RequestAborted));
    }

    private static async Task<IResult> ChangePasswordAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "currentPassword", "newPassword");
        var current = body.String("currentPassword", required: true);
        var next = body.String("newPassword", required: true);
        body.ThrowIfInvalid();

        await account.ChangePasswordAsync(current!, next!, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> MyLoginsAsync(HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        var logins = await account.MyLoginsAsync();
        httpContext.Response.Headers.CacheControl = "no-store";
        return AdminJson.Items(logins);
    }

    private static async Task<IResult> RemoveMyLoginAsync(string provider, HttpContext httpContext, [FromServices] MemberAccountService account)
    {
        ManagementQuery.Only(httpContext.Request);
        await account.RemoveMyLoginAsync(provider, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<string> EmailAsync(HttpContext httpContext)
    {
        ManagementQuery.Only(httpContext.Request);
        var body = await BodyReader.ReadAsync(httpContext.Request, "email");
        var email = body.String("email", required: true);
        body.ThrowIfInvalid();
        return email!;
    }

    // A member's own data is for that member, so nothing between it and the browser keeps a copy.
    private static IResult Private(HttpContext httpContext, MemberProfile profile)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return AdminJson.Ok(profile);
    }
}
