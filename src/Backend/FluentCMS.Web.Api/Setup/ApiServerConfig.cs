namespace FluentCMS.Web.Api;

public class ApiServerConfig
{
    public DatabaseConfig Database { get; set; } = default!;
    public class DatabaseConfig
    {
        public string ConnectionString { get; set; } = default!;
        public string Provider { get; set; } = default!;
    }
}
