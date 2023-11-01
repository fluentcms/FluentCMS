using FluentCMS.Entities.Sites;
using FluentCMS.Repository;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Application.Sites;
public class SiteService : ConstrainedGenericCrudServiceWithMapping<Site, SiteDto>
{
    ISiteRepository _repository;
    public SiteService(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _repository = serviceProvider.GetRequiredService<ISiteRepository>();
    }

    public override async Task BeforeCreateAndUpdate(Site entity)
    {
        //avoid duplicate url

        foreach (var url in entity.Urls)
        {
            var site = await _repository.GetByUrl(url);
            if(site != null && site?.Id != entity.Id)
            {
                throw new ApplicationException($"url '{url}' already used in another site with id {site?.Id}");
            }
        }
        await base.BeforeCreateAndUpdate(entity);
    }
}
