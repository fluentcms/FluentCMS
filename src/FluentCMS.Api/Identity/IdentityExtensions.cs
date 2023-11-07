using AutoMapper;
using FluentCMS.Api.Models.Identity;
using FluentCMS.Entities.Users;
using FluentCMS.Services;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace FluentCMS.Api.Identity;
public static class IdentityExtensions
{
    public static WebApplication MapFluentIdentity(this WebApplication app)
    {
        app.MapPost("/auth/login", async (FluentLoginRequest request, IUserService userService, FluentSignInManager signInManager) =>
        {
            try
            {
                var user = await userService.GetByUsername(request.UserName);
                var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, true);
                if (signInResult.Succeeded)
                {

                    var claimsPrincipal = await signInManager.CreateUserPrincipalAsync(user);

                    return Results.SignIn(claimsPrincipal, authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
                }
                return Results.Unauthorized();
            }
            catch (ApplicationException ex)
            {

                return Results.BadRequest(ex.Message);
            }
        });
        app.MapPost("/auth/register", async (FluentRegisterRequest request, IMapper mapper, FluentUserManager userManager) =>
        {

            var user = new User() { Username = request.Username };
            var result = await userManager.CreateAsync(user, request.Password);
            if (result.Succeeded)
            {
                return Results.Ok();
            }
            return Results.BadRequest(result.Errors);
        });
        app.MapPost("/auth/refresh", async (FluentRefreshRequest request, FluentUserManager userManager, FluentSignInManager signInManager, IOptions<BearerTokenOptions> options, TimeProvider timeProvider) =>
        {
            var protector = options.Value.RefreshTokenProtector;
            var ticket = protector.Unprotect(request.RefreshToken);
            if(ticket is null)
            {
                return Results.Unauthorized();
            }
            var expiresUtc = ticket?.Properties?.ExpiresUtc;
            if (expiresUtc != null && expiresUtc < timeProvider.GetUtcNow() || await signInManager.ValidateSecurityStampAsync(ticket.Principal) == null)
            {
                return Results.Unauthorized();
            }
            var userName = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await userManager.FindByNameAsync(userName);
            var newPrincipal = await signInManager.CreateUserPrincipalAsync(user);
            return Results.SignIn(newPrincipal, authenticationScheme: IdentityConstants.BearerScheme);
        });
        return app;
    }
}