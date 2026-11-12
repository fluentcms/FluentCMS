using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FluentCms")
    ?? throw new InvalidOperationException("ConnectionStrings:FluentCms is not set.");

builder.Services.AddFluentCms(cms => cms.UseSqlite(connectionString));

await using var app = builder.Build();
app.UseFluentCms();
app.MapFluentCms();

if (await app.RunFluentCmsCommandAsync(args) is { } exitCode)
{
    return exitCode;
}

await app.RunAsync();
return 0;
