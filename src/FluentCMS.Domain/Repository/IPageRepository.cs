using FluentCMS.Entities.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository;
public interface IPageRepository:IGenericRepository<Page>
{
    public Task<Page> FindByPath(string path);
}
