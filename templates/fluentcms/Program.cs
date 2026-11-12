using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Schema;
using FluentCmsApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(services => CmsSetup.BuildSchema(services.GetRequiredService<IFieldTypeRegistry>()));
builder.Services.AddFluentCms(cms => CmsSetup.Configure(cms, builder.Configuration.GetConnectionString(CmsSetup.ConnectionStringName)));

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

await app.RunAsync();
return 0;
