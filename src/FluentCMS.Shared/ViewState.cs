namespace FluentCMS;

public class ViewState : IDisposable
{
    public ViewStateType Type { get; set; } = ViewStateType.Default;
    public SiteViewState Site { get; set; } = default!;
    public PageViewState Page { get; set; } = default!;
    public LayoutViewState Layout { get; set; } = default!;
    public LayoutViewState DetailLayout { get; set; } = default!;
    public LayoutViewState EditLayout { get; set; } = default!;
    public List<PluginViewState> Plugins { get; set; } = default!;
    public UserViewState User { get; set; } = default!;
    public PluginViewState? Plugin { get; set; }
    public string? PluginViewName { get; set; }

    public event EventHandler? OnStateChanged;

    public Action ReloadAction { get; set; } = default!;
    public Action DisposeAction { get; set; } = default!;

    public void Reload()
    {
        ReloadAction?.Invoke();
        StateChanged();
    }

    public void StateChanged()
    {
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        DisposeAction?.Invoke();
    }
}

public enum ViewStateType
{
    Default,
    PluginEdit,
    PluginDetail,
    PagePreview,
    PageEdit
}

public class SiteViewState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public List<string> Urls { get; set; } = default!;
    public string? Description { get; set; }
    public List<RoleViewState> AllRoles { get; set; } = [];
    public Dictionary<string, string> Settings { get; set; } = [];
    public bool HasAdminAccess { get; set; }
    public bool HasContributorAccess { get; set; }
}

public class RoleViewState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public RoleTypesViewState Type { get; set; }
}

public enum RoleTypesViewState
{
    UserDefined = 0, // user defined roles
    Administrators = 1, // system defined role for administrators
    Authenticated = 2, // system defined role for authenticated users (logged in users)
    Guest = 3, // system defined role for unauthenticated users (guests)
    AllUsers = 4 // system defined role for all users including guests and authenticated users
}

public class LayoutViewState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Head { get; set; } = default!;
}

public class PageViewState
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Order { get; set; }
    public string Path { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool Locked { get; set; } = false;
    public Dictionary<string, string> Settings { get; set; } = [];
    public bool HasAdminAccess { get; set; }
    public bool HasViewAccess { get; set; }
    public string? Slug { get; set; }
}

public class PluginViewState
{
    public Guid Id { get; set; }
    public int Order { get; set; } = 0;
    public string Section { get; set; } = default!;
    public PluginDefinitionViewState Definition { get; set; } = default!;
    public bool Locked { get; set; } = false;
    public int Cols { get; set; } = 12;
    public int ColsMd { get; set; } = 0;
    public int ColsLg { get; set; } = 0;
    public Dictionary<string, string> Settings { get; set; } = [];
}

public class PluginDefinitionViewState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Description { get; set; }
    public List<PluginDefinitionTypeViewState> Types { get; set; } = default!;
    public bool Locked { get; set; } = false;
    public List<string> Stylesheets { get; set; } = [];

}

public class PluginDefinitionTypeViewState
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool IsDefault { get; set; } = false;
}

public class UserViewState
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<RoleViewState> Roles { get; set; } = [];
    public bool IsSuperAdmin { get; set; }
}
