namespace Admin.Api;

public class ApiClientFactory
{
    private readonly ApiClient _api;
    public ApiClientFactory(HttpClient http)
    {
        _api = new ApiClient(http);
        Users = new UserService(_api);
        Sites = new SiteService(_api);
        Roles = new RoleService(_api);
    }

    public UserService Users { get; }
    public SiteService Sites { get; }
    public RoleService Roles { get; }
}


