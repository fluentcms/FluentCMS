namespace Admin.Api;

using Admin.Api.ApiModels;

public class UserService
{
    private readonly ApiClient _api;
    public UserService(ApiClient api) => _api = api;

    public async Task<ListApiResponse<UserDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<UserDto>(
            "/api/Users/GetAll"
        );
        return res;
    }

    public async Task<ApiResponse<UserDto>> GetByIdAsync(Guid id)
    {
        var res = await _api.GetAsync<UserDto>($"/api/Users/GetById/{id}");
        return res;
    }

    public async Task<ApiResponse<UserDto>> UpdateAsync(string id, UserAddRequest model)
    {
        var res = await _api.PutAsync<UserAddRequest, UserDto>($"/api/Users/Update/{id}", model);
        return res;
    }

    public async Task<ApiResponse<UserDto>> CreateAsync(UserAddRequest model)
    {
        var res = await _api.PostAsync<UserAddRequest, UserDto>($"/api/Users/Add", model);
        return res;
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var res = await _api.DeleteAsync<bool>($"/api/Users/Remove/{id}");
        return res;
    }
}


public class RoleService
{
    private readonly ApiClient _api;
    public RoleService(ApiClient api) => _api = api;

    public async Task<ListApiResponse<RoleDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<RoleDto>(
            "/api/Roles/GetAll"
        );
        return res;
    }

    public async Task<ApiResponse<RoleDto>> GetByIdAsync(Guid id)
    {
        var res = await _api.GetAsync<RoleDto>($"/api/Roles/GetById/{id}");
        return res;
    }

    public async Task<ApiResponse<RoleDto>> UpdateAsync(string id, RoleAddRequest model)
    {
        var res = await _api.PutAsync<RoleAddRequest, RoleDto>($"/api/Roles/Update/{id}", model);
        return res;
    }

    public async Task<ApiResponse<RoleDto>> CreateAsync(RoleAddRequest model)
    {
        var res = await _api.PostAsync<RoleAddRequest, RoleDto>($"/api/Roles/Add", model);
        return res;
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var res = await _api.DeleteAsync<bool>($"/api/Roles/Remove/{id}");
        return res;
    }
}

public class SiteService
{
    private readonly ApiClient _api;
    public SiteService(ApiClient api) => _api = api;

    public async Task<List<SiteDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<SiteDto>(
            "/api/Site/GetAll"
        );
        return res.Data.ToList();
    }
}

