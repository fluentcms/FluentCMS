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
        CreateMap<PageSectionDetailResponse, PageSectionState>();
        CreateMap<PageRowDetailResponse, PageRowState>();
        CreateMap<PageColumnDetailResponse, PageColumnState>();
        CreateMap<PluginDefinitionDetailResponse, PluginDefinitionViewState>();
        CreateMap<PluginDefinitionType, PluginDefinitionTypeViewState>();
        CreateMap<UserLoginResponse, UserViewState>();

        #endregion

    }
}
