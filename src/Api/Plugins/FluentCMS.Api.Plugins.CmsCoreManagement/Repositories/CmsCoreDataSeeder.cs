namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

internal class CmsCoreDataSeeder(CmsCoreDbContext dbContext, ILogger<CmsCoreDataSeeder> logger)
    : BaseDataSeeder<CmsCoreDbContext>(dbContext, logger)
{
    public override int Priority => 10000;

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Seeding initial site entities into the database...");

        var sites = new[]
        {
            new
            {
                Name = "Default Site",
                Url = "localhost:5022",
                Title = "Default CMS Site",
                Emoji = "🌐",
                Pages = new[]
                {
                    ("Home", ""),
                    ("About Us", "about"),
                    ("Contact", "contact"),
                    ("Blog", "blog")
                },
                Folders = new[]
                {
                    "Images",
                    "Documents",
                    "Videos",
                    "Temp",
                    "Archive"
                }
            },
            new
            {
                Name = "Marketing Hub",
                Url = "127.0.0.1:5022",
                Title = "Marketing Hub",
                Emoji = "📣",
                Pages = new[]
                {
                    ("Home", ""),
                    ("Campaigns", "campaigns"),
                    ("Landing Pages", "landing-pages"),
                    ("Case Studies", "case-studies"),
                    ("Team", "team")
                },
                Folders = new[]
                {
                    "Images",
                    "Brand",
                    "Ads",
                    "EmailTemplates",
                    "Analytics"
                }
            },
            new
            {
                Name = "Docs Portal",
                Url = "0.0.0.0:5022",
                Title = "Documentation Portal",
                Emoji = "📄",
                Pages = new[]
                {
                    ("Home", ""),
                    ("Getting Started", "getting-started"),
                    ("API Reference", "api"),
                    ("Guides", "guides"),
                    ("Changelog", "changelog"),
                    ("FAQ", "faq")
                },
                Folders = new[]
                {
                    "Guides",
                    "ApiSamples",
                    "Releases",
                    "Images",
                    "Internal"
                }
            }
        };

        foreach (var s in sites)
        {
            var site = new Site
            {
                Name = s.Name,
                Urls = [s.Url],
                Description = $"{s.Name} seeded site {s.Emoji}",
                MetaTitle = s.Title,
                MetaDescription = $"{s.Name} instance",
                RobotsIndex = true,
                RobotsFollow = true,
                RobotsTxt = "User-agent: *\nAllow: /",
                OgType = "website",
                Head = $"<meta property=\"og:site_name\" content=\"{s.Title}\" />"
            };

            await DbContext.Sites.AddAsync(site, cancellationToken);
            await DbContext.SaveChangesAsync(cancellationToken);

            var layout = new Layout
            {
                SiteId = site.Id,
                Name = $"{s.Name} Layout",
                Head = $"<title>{s.Title} {s.Emoji}</title>",
                Body = $"<h1>{s.Name} Layout Body {s.Emoji}</h1>"
            };

            await DbContext.Layouts.AddAsync(layout, cancellationToken);
            await DbContext.SaveChangesAsync(cancellationToken);

            site.LayoutId = layout.Id;
            site.EditLayoutId = layout.Id;
            site.DetailLayoutId = layout.Id;

            DbContext.Sites.Update(site);
            await DbContext.SaveChangesAsync(cancellationToken);


            var order = 0;
            foreach (var p in s.Pages)
            {
                var page = new Page
                {
                    SiteId = site.Id,
                    Title = p.Item1 + " " + s.Emoji,
                    Slug = p.Item2,
                    Order = order++,
                    LayoutId = layout.Id,
                    EditLayoutId = layout.Id,
                    DetailLayoutId = layout.Id,
                    MetaTitle = p.Item1,
                    RobotsIndex = true,
                    RobotsFollow = true,
                    OgType = "website"
                };

                await DbContext.Pages.AddAsync(page, cancellationToken);
            }

            var rootFolder = new Folder
            {
                SiteId = site.Id,
                Name = "Files",
                NormalizedName = "files",
                ParentId = null,
                Size = 0
            };

            await DbContext.Folders.AddAsync(rootFolder, cancellationToken);

            var childFolders = new List<Folder>();

            foreach (var f in s.Folders)
            {
                childFolders.Add(new Folder
                {
                    SiteId = site.Id,
                    Name = f,
                    NormalizedName = f.ToLower(),
                    ParentId = rootFolder.Id,
                    Size = 0
                });
            }

            await DbContext.Folders.AddRangeAsync(childFolders, cancellationToken);

            var nested = new List<Folder>();

            foreach (var cf in childFolders.Take(2))
            {
                nested.Add(new Folder
                {
                    SiteId = site.Id,
                    Name = $"{cf.Name} Sub",
                    NormalizedName = $"{cf.NormalizedName}-sub",
                    ParentId = cf.Id,
                    Size = 0
                });
            }

            await DbContext.Folders.AddRangeAsync(nested, cancellationToken);

            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
