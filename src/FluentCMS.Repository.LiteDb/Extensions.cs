using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repository.LiteDb;

public static class Extensions
{
    public static FluentCMSBuilder AddLiteDbRepository(this FluentCMSBuilder fcBuilder, Action<LiteDbOptionsBuilder> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(LiteDbOptionsBuilder));

        var optionsBuilder = new LiteDbOptionsBuilder();
        configure(optionsBuilder);

        return fcBuilder.AddLiteDbRepository(optionsBuilder.Build());
    }

    public static FluentCMSBuilder AddLiteDbRepository(this FluentCMSBuilder fcBuilder, LiteDbOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(LiteDbOptions));

        fcBuilder.Services.AddScoped<LiteDbContext>(p => new LiteDbContext(options));

        fcBuilder.Services.AddScoped(typeof(IGenericRepository<>), typeof(LiteDbGenericRepository<>));
        fcBuilder.Services.AddScoped(typeof(IGenericRepository<,>), typeof(LiteDbGenericRepository<,>));

        fcBuilder.Services.AddScoped<IUserRepository, LiteDbUserRepository>();
        fcBuilder.Services.AddScoped<IContentTypeRepository, LiteDbContentTypeRepository>();
        fcBuilder.Services.AddScoped<ISiteRepository, LiteDbSiteRepository>();
        fcBuilder.Services.AddScoped<IPageRepository, LiteDbPageRepository>();

        return fcBuilder;
    }
}
