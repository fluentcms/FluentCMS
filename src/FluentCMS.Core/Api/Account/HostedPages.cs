using FluentCMS.Core.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Account;

// The hosted pages of spec 4.7: Razor Pages of the FluentCms area, compiled into this assembly and routed at /account. A
// host overrides one, or the layout, by adding a page at the same path to its own project, such as
// Areas/FluentCms/Pages/Account/Login.cshtml: the host's application part comes first, and the first page of a path wins.
internal static class HostedPages
{
    public const string Area = "FluentCms";

    // No script runs on the pages; the style sheet is inline, and the logo is an https URL or on this host.
    internal const string ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src 'self' https:; base-uri 'none'";

    public static void Add(IServiceCollection services)
    {
        var assembly = typeof(HostedPages).Assembly;
        services.AddRazorPages(options => options.Conventions.AddAreaFolderApplicationModelConvention(
                Area,
                "/",
                model => model.Filters.Add(new PolicyFilter())))
            .ConfigureApplicationPartManager(manager =>
            {
                if (!manager.ApplicationParts.OfType<AssemblyPart>().Any(part => part.Assembly == assembly))
                {
                    foreach (var part in ApplicationPartFactory.GetApplicationPartFactory(assembly).GetApplicationParts(assembly))
                    {
                        manager.ApplicationParts.Add(part);
                    }
                }
            });
    }

    // The pages share the data source of every Razor page of the host, so a host that maps its own pages too gets each
    // page once. The /account conventions (the FluentCMS headers with frame-ancestors, and the auth rate limit by address)
    // are applied to the pages of the area only, a host's override included.
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder endpoints)
    {
        var pages = new AreaConventionBuilder(endpoints.MapRazorPages());
        pages.AsHostedPages().WithMetadata(new FluentCmsSurfaceMetadata("account"));
        return pages;
    }

    internal static bool IsHostedPage(EndpointBuilder endpoint) =>
        endpoint.Metadata.OfType<PageActionDescriptor>().Any(page => string.Equals(page.AreaName, Area, StringComparison.Ordinal));

    private sealed class AreaConventionBuilder(IEndpointConventionBuilder pages) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention) =>
            pages.Add(endpoint =>
            {
                if (IsHostedPage(endpoint))
                {
                    convention(endpoint);
                }
            });

        public void Finally(Action<EndpointBuilder> finallyConvention) =>
            pages.Finally(endpoint =>
            {
                if (IsHostedPage(endpoint))
                {
                    finallyConvention(endpoint);
                }
            });
    }

    // A page that set its own policy keeps it.
    private sealed class PolicyFilter : IResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            var headers = context.HttpContext.Response.Headers;
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
            }
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }
}
