using System.Text.Json.Nodes;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Data.Sqlite;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The OpenAPI document of a Development host with the Blog sample and the first-party plugins, mapped as the reference
// host's Program.cs maps it; the test host does not map it.
internal static class OpenApiDocument
{
    public static async Task<JsonObject> ReadAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("fluentcms-admin-openapi-");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FluentCms:Security:AllowUnencryptedKeyRing"] = "true",
                ["FluentCms:Media:Local:Root"] = Path.Combine(directory.FullName, "media"),
            });
            builder.Services.AddFluentCms(cms => cms.UseSqlite($"Data Source={Path.Combine(directory.FullName, "cms.db")};Pooling=False"));
            builder.Services.AddSingleton(services =>
            {
                var schema = new SchemaBuilder(services.GetRequiredService<IFieldTypeRegistry>());
                foreach (var type in BlogSample.ContentTypes)
                {
                    schema.AddContentType(type);
                }

                return schema.Build();
            });

            await using var app = builder.Build();
            app.UseFluentCms();
            app.MapFluentCmsOpenApi();
            app.MapFluentCms();
            await app.StartAsync(token);
            try
            {
                using var client = app.GetTestClient();
                var text = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), token);
                return JsonNode.Parse(text)!.AsObject();
            }
            finally
            {
                await app.StopAsync(token);
            }
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
