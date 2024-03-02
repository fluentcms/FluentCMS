using System.Text.Json;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using FluentCMS.Web.UI;
using FluentCMS.Web.UI.Services.LocalStorage;

namespace Microsoft.Extensions.DependencyInjection;

public static class SiteServiceExtensions
{
    public static IServiceCollection AddSiteServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAdminUIServices(configuration);

        // Add services to the container.
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return services;
    }

    public static IApplicationBuilder UseSiteServices(this WebApplication app)
    {
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapGet("/api/auth", async (HttpContext context) =>
        {
            var data = new UserLoginResponse()
            {
                UserId = Guid.Parse(context.Request.Query["user-id"]),
                Token = context.Request.Query["token"],
                RoleIds = JsonSerializer.Deserialize<List<Guid>>(context.Request.Query["role-ids"]),
            };
            var tokenProvider = context.RequestServices.GetRequiredService<IUserTokenProvider>(); ;
            await tokenProvider.Validate(data.Token);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            context.Response.Cookies.Append("UserLoginResponse", json);
            context.Response.Redirect("/");
        });

        app.MapGet("/api/logout", async (context) =>
        {
            context.Response.Cookies.Delete("UserLoginResponse");
            context.Response.Redirect("/");
        });

        return app;
    }

}
