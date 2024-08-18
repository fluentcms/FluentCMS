namespace FluentCMS;

public class ViewState
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
}

public class PluginDefinitionViewState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Description { get; set; }
    public List<PluginDefinitionTypeViewState> Types { get; set; } = default!;
    public bool Locked { get; set; } = false;

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
}
