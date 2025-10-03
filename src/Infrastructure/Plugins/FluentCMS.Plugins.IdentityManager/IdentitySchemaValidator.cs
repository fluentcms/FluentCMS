namespace FluentCMS.Plugins.IdentityManager;

internal class IdentitySchemaValidator(ApplicationDbContext dbContext, ILogger<IdentitySchemaValidator> logger) : EfSchemaValidator<ApplicationDbContext>(dbContext, logger)
{
    public override int Priority => 1000;

    public override async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        var sql = DbContext.Database.GenerateCreateScript();
        await DbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}

