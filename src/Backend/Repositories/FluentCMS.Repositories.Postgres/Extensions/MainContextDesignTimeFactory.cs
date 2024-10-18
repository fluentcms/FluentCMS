using FluentCMS.Web.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Design;

namespace FluentCMS.Repositories.Postgres.Extensions;

public class MainContextDesignTimeFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args)
    {

        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=fluentcms;Username=postgres;Password=postgres");

        return new(optionsBuilder.Options, new ApiExecutionContext(new HttpContextAccessor()));
    }
}
