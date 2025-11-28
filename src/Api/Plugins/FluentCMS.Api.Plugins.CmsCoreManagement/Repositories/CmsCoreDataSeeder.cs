namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

internal class CmsCoreDataSeeder(CmsCoreDbContext dbContext, ILogger<CmsCoreDataSeeder> logger) 
    : BaseDataSeeder<CmsCoreDbContext>(dbContext, logger)
{
    public override int Priority => 10000;

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Seeding initial site entities into the database...");

        var defaultLayout = new Layout
        {
            Name = "DefaultLayout",
            Head = "<title>Default layout head</title>",
            Body = "<h1>Default layout body</h1>"
        };

        await DbContext.Layouts.AddAsync(defaultLayout, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);

        var site = new Site
        {
            Name = "Default Site",
            Urls = ["http://localhost"],
            Description = "Initial seeded website",
            LayoutId = defaultLayout.Id,
            EditLayoutId = defaultLayout.Id,
            DetailLayoutId = defaultLayout.Id,
            MetaTitle = "Default CMS Site",
            MetaDescription = "Seeded default CMS instance",
            RobotsIndex = true,
            RobotsFollow = true,
            RobotsTxt = "User-agent: *\nAllow: /",
            GoogleTagsId = null,
            OgType = "website",
            Head = "<meta property=\"og:site_name\" content=\"Default CMS Site\" />"
        };

        await DbContext.Sites.AddAsync(site, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);

        var homePage = new Page
        {
            SiteId = site.Id,
            Title = "Home",
            Slug = "",
            Order = 0,
            LayoutId = defaultLayout.Id,
            EditLayoutId = defaultLayout.Id,
            DetailLayoutId = defaultLayout.Id,
            MetaTitle = "Home",
            RobotsIndex = true,
            RobotsFollow = true,
            OgType = "website"
        };

        await DbContext.Pages.AddAsync(homePage, cancellationToken);

        var rootFolder = new Folder
        {
            SiteId = site.Id,
            Name = "Files",
            NormalizedName = "files",
            ParentId = null,
            Size = 0
        };

         // First-level subfolders
        var imagesFolder = new Folder { SiteId = site.Id, Name = "Images", NormalizedName = "images", ParentId = rootFolder.Id, Size = 0 };
        var documentsFolder = new Folder { SiteId = site.Id, Name = "Documents", NormalizedName = "documents", ParentId = rootFolder.Id, Size = 0 };
        var videosFolder = new Folder { SiteId = site.Id, Name = "Videos", NormalizedName = "videos", ParentId = rootFolder.Id, Size = 0 };

        await DbContext.Folders.AddRangeAsync(new[] { imagesFolder, documentsFolder, videosFolder }, cancellationToken);

        // Second-level nested folders under Images
        var logosFolder = new Folder { SiteId = site.Id, Name = "Logos", NormalizedName = "logos", ParentId = imagesFolder.Id, Size = 0 };
        var bannersFolder = new Folder { SiteId = site.Id, Name = "Banners", NormalizedName = "banners", ParentId = imagesFolder.Id, Size = 0 };

        // Second-level nested folders under Documents
        var contractsFolder = new Folder { SiteId = site.Id, Name = "Contracts", NormalizedName = "contracts", ParentId = documentsFolder.Id, Size = 0 };
        var reportsFolder = new Folder { SiteId = site.Id, Name = "Reports", NormalizedName = "reports", ParentId = documentsFolder.Id, Size = 0 };

        // Third-level nested folder under Reports
        var annualReportsFolder = new Folder { SiteId = site.Id, Name = "Annual", NormalizedName = "annual", ParentId = reportsFolder.Id, Size = 0 };
        var monthlyReportsFolder = new Folder { SiteId = site.Id, Name = "Monthly", NormalizedName = "monthly", ParentId = reportsFolder.Id, Size = 0 };
        var weeklyReportsFolder = new Folder { SiteId = site.Id, Name = "Weekly", NormalizedName = "weekly", ParentId = reportsFolder.Id, Size = 0 };

        await DbContext.Folders.AddRangeAsync(new[]
        {
            logosFolder, bannersFolder,
            contractsFolder, reportsFolder,
            annualReportsFolder,
            monthlyReportsFolder,
            weeklyReportsFolder,
        }, cancellationToken);

        await DbContext.Folders.AddAsync(rootFolder, cancellationToken);

        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
