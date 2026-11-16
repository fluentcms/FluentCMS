using FluentCMS.Admin.Api;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace FluentCMS.Admin;

public static class AdminHostBuilderExtensions
{
    // Asynchronous because it will read config.json before anything else registers (spec 2.5).
    public static Task AddFluentCmsAdmin(this WebAssemblyHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddManagementApi();
        return Task.CompletedTask;
    }
}
