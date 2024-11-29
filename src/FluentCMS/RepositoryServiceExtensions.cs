public static class RepositoryServiceExtensions
{
    public static void AddRepositoryServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRepositoryServices(configuration, "DefaultConnection");
    }

    public static void AddRepositoryServices(this IServiceCollection services, IConfiguration configuration, string connectionStringName)
    {
        var dbType = configuration["Database"];

        switch (dbType?.ToLower())
        {
            case "litedb":
                services.AddLiteDbRepositories(connectionStringName);
                break;
            case "mongodb":
                services.AddMongoDbRepositories(connectionStringName);
                break;
            case "sqlite":
                services.AddSqliteRepositories(connectionStringName);
                break;
            case "sqlserver":
                services.AddSqlServerRepositories(connectionStringName);
                break;
            //case "mysql":
            //    services.AddMySqlRepositories(connectionStringName);
            //    break;
            case "postgresql":
                services.AddPostgresRepositories(connectionStringName);
                break;
            default:
                throw new InvalidOperationException($"Unsupported database type: {dbType}");
        }
    }
}
