using Ardalis.GuardClauses;
using FluentCMS.Entities.Pages;
using FluentCMS.Entities.Sites;
using FluentCMS.Repository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Services;
public class PageService(IPageRepository _repository)
{

    public async Task<Page> Create(string title, Guid siteId, Guid? parentId, string? path = null, int? order = null)
    {
        Guard.Against.NullOrEmpty(title);
        var id = Guid.NewGuid();
        if (path == null) { path = $"/{GetSlug(title)}"; }
        await ThrowIfDuplicatePath(_repository, siteId, path, id);
        if (order == null) { order = await GetLastOrder(_repository) + 1; }
        return new Page(id, siteId, path, parentId, title, order.Value);
    }

    private static async Task ThrowIfDuplicatePath(IPageRepository _repository, Guid siteId, string path, Guid id)
    {
        if (await AnyDuplicatePathInSite(_repository, siteId, path, id))
        {
            throw new ApplicationException("A page with the same path already exists.");
        }
    }
    public async Task ValidateEdit(Page page)
    {
        await ThrowIfDuplicatePath(_repository, page.SiteId, page.Path, page.Id);
    }

    private static async Task<bool> AnyDuplicatePathInSite(IPageRepository _repository, Guid siteId, string path, Guid id)
    {
        return (await _repository.GetAll(x => x.Path == path && x.SiteId == siteId && x.Id != id)).Any();
    }

    private static async Task<int> GetLastOrder(IPageRepository _repository)
    {
        return (await _repository.GetAll()).Select(x=>x.Order).DefaultIfEmpty(-1).Max();
    }

    private static string GetSlug(string title)
    {
        return title.Trim().ToLower().Replace(" ", "-");
    }
}
