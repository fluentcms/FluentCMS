using AutoMapper;
using FluentCMS.Entities.Sites;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Application.Sites;
public class SiteMappingProfile:Profile
{
    public SiteMappingProfile()
    {
        CreateMap<SiteDto,Site>().ReverseMap();
    }
}
