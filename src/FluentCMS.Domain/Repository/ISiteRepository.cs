using FluentCMS.Entities.Sites;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository;
public interface ISiteRepository:IGenericRepository<Site>
{
    Task<Site> GetByUrl(string url);
}
