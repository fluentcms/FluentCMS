using FluentCMS.Entities.Sites;
using FluentCMS.Entities.Pages;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace FluentCMS.Api;

public static class SeedData
{
    // TODO: in production we should delete the folder after seeding
    public static void SeedDefaultData(this IServiceProvider provider, string dataFolder)
    {
        try
        {
            var scope = provider.CreateScope();

            var siteService = scope.ServiceProvider.GetRequiredService<ISiteService>();
            var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();

            if (!siteService.GetAll().GetAwaiter().GetResult().Any())
            {
                // read site.json
                // TODO: check this for the file path on deployment
                var site = LoadData<Site>($@"{dataFolder}\site.json");
                if (site == null)
                    return;

                siteService.Create(site).GetAwaiter().GetResult();

                var pages = LoadData<List<Page>>($@"{dataFolder}\pages.json");
                if (pages == null)
                    return;

                foreach (var page in pages)
                {
                    page.SiteId = site.Id;
                    pageService.Create(page).GetAwaiter().GetResult();
                }
            }

        }
        catch (Exception)
        {
            return;
        }
    }

    private static T? LoadData<T>(string jsonFile)
    {
        try
        {
            var json = File.ReadAllText(jsonFile);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception)
        {
            return default;
        }
    }

}
