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

        public async Task<ListApiResponse<SiteDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<SiteDto>(
            "/api/Sites/GetAll"
        );
        return res;
    }

    public async Task<ApiResponse<SiteDto>> GetByIdAsync(Guid id)
    {
        var res = await _api.GetAsync<SiteDto>($"/api/Sites/GetById/{id}");
        return res;
    }

    public async Task<ApiResponse<SiteDto>> GetByUrlAsync(string url)
    {
        var res = await _api.GetAsync<SiteDto>($"/api/Sites/GetByUrl?url={url}");
        return res;
    }

    public async Task<ApiResponse<SiteDto>> UpdateAsync(string id, SiteUpdateRequest model)
    {
        var res = await _api.PutAsync<SiteUpdateRequest, SiteDto>($"/api/Sites/Update/{id}", model);
        return res;
    }

    public async Task<ApiResponse<SiteDto>> CreateAsync(SiteAddRequest model)
    {
        var res = await _api.PostAsync<SiteAddRequest, SiteDto>($"/api/Sites/Add", model);
        return res;
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var res = await _api.DeleteAsync<bool>($"/api/Sites/Remove/{id}");
        return res;
    }
}


public class PageService
{
    private readonly ApiClient _api;
    public PageService(ApiClient api) => _api = api;

        public async Task<ListApiResponse<PageDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<PageDto>(
            "/api/Pages/GetAll"
        );
        return res;
    }

    public async Task<ListApiResponse<PageDto>> GetBySiteIdAsync(Guid siteId)
    {
        var res = await _api.GetListAsync<PageDto>(
            "/api/Pages/GetBySiteId?siteId=" + siteId
        );
        return res;
    }

    public async Task<ApiResponse<PageDto>> GetByIdAsync(Guid id)
    {
        var res = await _api.GetAsync<PageDto>($"/api/Pages/GetById/{id}");
        return res;
    }

    public async Task<ApiResponse<PageDto>> UpdateAsync(string id, PageUpdateRequest model)
    {
        var res = await _api.PutAsync<PageUpdateRequest, PageDto>($"/api/Pages/Update/{id}", model);
        return res;
    }

    public async Task<ApiResponse<PageDto>> CreateAsync(PageAddRequest model)
    {
        var res = await _api.PostAsync<PageAddRequest, PageDto>($"/api/Pages/Add", model);
        return res;
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var res = await _api.DeleteAsync<bool>($"/api/Pages/Remove/{id}");
        return res;
    }
}

public class LayoutService
{
    private readonly ApiClient _api;
    public LayoutService(ApiClient api) => _api = api;

    public async Task<ListApiResponse<LayoutDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<LayoutDto>(
            "/api/Layouts/GetAll"
        );
        return res;
    }

    public async Task<ListApiResponse<LayoutDto>> GetBySiteIdAsync(Guid siteId)
    {
        var res = await _api.GetListAsync<LayoutDto>(
            "/api/Layouts/GetBySiteId?siteId=" + siteId
        );
        return res;
    }

    public async Task<ApiResponse<LayoutDto>> GetByIdAsync(Guid id)
    {
        var res = await _api.GetAsync<LayoutDto>($"/api/Layouts/GetById/{id}");
        return res;
    }

    public async Task<ApiResponse<LayoutDto>> UpdateAsync(string id, LayoutUpdateRequest model)
    {
        var res = await _api.PutAsync<LayoutUpdateRequest, LayoutDto>($"/api/Layouts/Update/{id}", model);
        return res;
    }

    public async Task<ApiResponse<LayoutDto>> CreateAsync(LayoutAddRequest model)
    {
        var res = await _api.PostAsync<LayoutAddRequest, LayoutDto>($"/api/Layouts/Add", model);
        return res;
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var res = await _api.DeleteAsync<bool>($"/api/Layouts/Remove/{id}");
        return res;
    }
}



public class FolderService
{
    private readonly ApiClient _api;
    public FolderService(ApiClient api) => _api = api;

    public async Task<ListApiResponse<FolderDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<FolderDto>(
            "/api/Folders/GetAll"
        );
        return res;
    }
    public async Task<ListApiResponse<FolderDto>> GetParentFoldersAsync(Guid folderId)
    {
        var res = await _api.GetListAsync<FolderDto>(
            "/api/Folders/GetParentFolders?folderId=" + folderId.ToString()
        );
        return res;
    }

    public async Task<ApiResponse<FolderDto>> GetByIdAsync(Guid id)
    {
        var res = await _api.GetAsync<FolderDto>($"/api/Folders/GetById/{id}");
        return res;
    }


    public async Task<ApiResponse<FolderDto>> GetRootAsync(Guid siteId)
    {
        var url = $"/api/Folders/GetRoot?siteId=" + siteId.ToString();
        Console.WriteLine("Api service: GetRootAsync: " + siteId.ToString());
        Console.WriteLine("Api service: URL: " + url);

        var res = await _api.GetAsync<FolderDto>(url);
        return res;
    }

    public async Task<ApiResponse<FolderDto>> MoveAsync(FolderMoveRequest model)
    {
        var res = await _api.PutAsync<FolderMoveRequest, FolderDto>($"/api/Folders/Move", model);
        return res;
    }

    public async Task<ApiResponse<FolderDto>> RenameAsync(FolderRenameRequest model)
    {
        var res = await _api.PutAsync<FolderRenameRequest, FolderDto>($"/api/Folders/Rename", model);
        return res;
    }

    public async Task<ApiResponse<FolderDto>> CreateAsync(FolderAddRequest model)
    {
        var res = await _api.PostAsync<FolderAddRequest, FolderDto>($"/api/Folders/Create", model);
        return res;
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var res = await _api.DeleteAsync<bool>($"/api/Folders/Remove/{id}");
        return res;
    }
}


public class FileService
{
    private readonly ApiClient _api;
    public FileService(ApiClient api) => _api = api;

    public async Task<ListApiResponse<FileDto>> GetAllAsync()
    {
        var res = await _api.GetListAsync<FileDto>(
            "/api/Folders/GetAll"
        );
        return res;
    }

    public async Task<ApiResponse<FileDto>> GetByIdAsync(Guid id)
    {
        var res = await _api.GetAsync<FileDto>($"/api/Folders/GetById/{id}");
        return res;
    }

    public async Task<ApiResponse<List<FileDto>>> UploadAsync(Guid folderId, List<FileParameter> files)
    {
        var res = await _api.PostFilesAsync(folderId, files);
        return res;
    }

    public async Task<ApiResponse<FileDto>> MoveAsync(FileMoveRequest model)
    {
        var res = await _api.PutAsync<FileMoveRequest, FileDto>($"/api/Files/Move", model);
        return res;
    }

    public async Task<ApiResponse<FileDto>> RenameAsync(FileRenameRequest model)
    {
        var res = await _api.PutAsync<FileRenameRequest, FileDto>($"/api/Files/Rename", model);
        return res;
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
    {
        var res = await _api.DeleteAsync<bool>($"/api/Files/Remove/{id}");
        return res;
    }
}