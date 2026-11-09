using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

[Collection(ProcessEnvironment.Collection)]
public sealed class EnvironmentVariableTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task AnEnvironmentVariableReachesTheOptionsThroughTheDoubleUnderscore()
    {
        const string Variable = "FluentCms__Database__MigrateOnStartup";
        Environment.SetEnvironmentVariable(Variable, "false");
        try
        {
            var app = _host.Build(Environments.Production);

            app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup.ShouldBe(false);

            // Nothing migrates a new database at start, so the system stream is pending.
            var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));
            exception.Code.ShouldBe("FCMS1305");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }
}
