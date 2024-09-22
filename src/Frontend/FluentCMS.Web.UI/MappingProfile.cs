﻿using AutoMapper;

namespace FluentCMS.Web.UI;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        #region ViewState

        CreateMap<PageFullDetailResponse, PageViewState>();
        CreateMap<SiteDetailResponse, SiteViewState>();
        CreateMap<LayoutDetailResponse, LayoutViewState>();
        CreateMap<PluginDetailResponse, PluginViewState>();
        CreateMap<PluginDetailResponse, PluginUpdateRequest>();
        CreateMap<PluginDefinitionDetailResponse, PluginDefinitionViewState>();
        CreateMap<PluginDefinitionType, PluginDefinitionTypeViewState>();
        CreateMap<RoleDetailResponse, RoleViewState>();
        CreateMap<UserRoleDetailResponse, UserViewState>();

        #endregion

    }
}
