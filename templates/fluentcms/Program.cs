#if (admin)
using FluentCMS.Admin.Hosting;
#endif
using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Schema;
using FluentCms_App;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(services => CmsSetup.BuildSchema(services.GetRequiredService<IFieldTypeRegistry>()));
#if (admin)
builder.Services.AddFluentCms(cms =>
{
    CmsSetup.Configure(cms, builder.Configuration.GetConnectionString(CmsSetup.ConnectionStringName));

    // The admin and its sign-in client, while FluentCms:Admin:Enabled is true.
    cms.AddAdmin();
});
#else
builder.Services.AddFluentCms(cms => CmsSetup.Configure(cms, builder.Configuration.GetConnectionString(CmsSetup.ConnectionStringName)));
#endif

await using var app = builder.Build();

// "fluentcms <command>", as the fluentcms tool or an init container runs it, runs the command and ends with its exit code.
if (await app.RunFluentCmsCommandAsync(args) is { } exitCode)
{
    return exitCode;
}

app.UseFluentCms();
app.MapFluentCms();

// The OpenAPI document and its Scalar page; off outside Development unless FluentCms:OpenApi:Enabled is true.
app.MapFluentCmsOpenApi();
#if (admin)

// The admin at /admin: its page with the base set, its files, and its config.json built from FluentCms:Admin.
app.MapFluentCmsAdmin();
#endif

await app.RunAsync();
return 0;
