using FluentCMS.Plugins.Media;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.IntegrationTests.Blog;

// The Blog seed uploads its images through the media plugin. A test host keeps them in a directory of its own, removed
// with the host, rather than beside the test assemblies.
internal static class BlogMedia
{
    public static FluentCmsTestHost WithMediaDirectory(this FluentCmsTestHost host)
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-blog-media-");
        return host.WithServices(services =>
        {
            services.PostConfigure<MediaOptions>(options => options.Local.Root = directory.FullName);
            services.AddHostedService(_ => new Removal(directory));
        });
    }

    private sealed class Removal(DirectoryInfo directory) : IHostedService, IDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
