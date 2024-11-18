using AutoMapper;

namespace FluentCMS.Repositories.EFCore;

public class ContentTypeRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext, IMapper mapper) : IContentTypeRepository
{
    public async Task<ContentType?> Create(ContentType entity, CancellationToken cancellationToken = default)
    {
        var dbContentType = mapper.Map<DbModels.ContentType>(entity);

        dbContentType.Id = Guid.NewGuid();
        dbContentType.CreatedBy = apiExecutionContext.Username;
        dbContentType.CreatedAt = DateTime.UtcNow;

        await dbContext.ContentTypes.AddAsync(dbContentType, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<ContentType>(dbContentType);
    }

    public async Task<IEnumerable<ContentType>> CreateMany(IEnumerable<ContentType> entities, CancellationToken cancellationToken = default)
    {
        var dbContentTypes = mapper.Map<List<DbModels.ContentType>>(entities);
        foreach (var item in entities)
        {
            item.Id = Guid.NewGuid();
            item.CreatedBy = apiExecutionContext.Username;
            item.CreatedAt = DateTime.UtcNow;
        }
        await dbContext.ContentTypes.AddRangeAsync(dbContentTypes, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<IEnumerable<ContentType>>(dbContentTypes);
    }

    public async Task<ContentType?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var dbContentType = await dbContext.ContentTypes.FirstOrDefaultAsync(ct => ct.Id == id, cancellationToken);

        if (dbContentType == null)
            return null;

        dbContext.ContentTypes.Remove(dbContentType);
        await dbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<ContentType>(dbContentType);
    }

    public async Task<IEnumerable<ContentType>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var dbContentTypes = await dbContext.ContentTypes.Where(ct => ids.Contains(ct.Id)).ToListAsync(cancellationToken);

        if (dbContentTypes.Count == 0)
            return [];

        dbContext.ContentTypes.RemoveRange(dbContentTypes);
        await dbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<IEnumerable<ContentType>>(dbContentTypes);
    }

    public async Task<IEnumerable<ContentType>> GetAll(CancellationToken cancellationToken = default)
    {
        var dbContentTypes = await dbContext.ContentTypes.ToListAsync(cancellationToken);
        return mapper.Map<IEnumerable<ContentType>>(dbContentTypes);
    }

    public async Task<IEnumerable<ContentType>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        var dbContentTypes = await dbContext.ContentTypes.Where(x => x.SiteId == siteId).ToListAsync(cancellationToken);
        return mapper.Map<IEnumerable<ContentType>>(dbContentTypes);
    }

    public async Task<ContentType?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var dbContentType = await dbContext.ContentTypes.FirstOrDefaultAsync(ct => ct.Id == id, cancellationToken);
        return dbContentType != null ? mapper.Map<ContentType>(dbContentType) : null;
    }

    public async Task<IEnumerable<ContentType>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var dbContentTypes = await dbContext.ContentTypes.Where(ct => ids.Contains(ct.Id)).ToListAsync(cancellationToken);
        return mapper.Map<IEnumerable<ContentType>>(dbContentTypes);
    }

    public async Task<ContentType?> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        var dbContentType = await dbContext.ContentTypes.FirstOrDefaultAsync(ct => ct.SiteId == siteId && ct.Slug == contentTypeSlug, cancellationToken);
        return dbContentType != null ? mapper.Map<ContentType>(dbContentType) : null;
    }

    public async Task<ContentType?> Update(ContentType entity, CancellationToken cancellationToken = default)
    {
        var dbContentType = await dbContext.ContentTypes.FirstOrDefaultAsync(ct => ct.Id == entity.Id, cancellationToken);
        if (dbContentType == null)
            return null;

        var oldFields = await dbContext.ContentTypeFields.Where(cf => cf.ContentTypeId == entity.Id).ToListAsync(cancellationToken);
        // delete old fields
        foreach (var field in oldFields)
            dbContext.Entry(field).State = EntityState.Deleted;

        mapper.Map(entity, dbContentType); // Update dbEntity with the new data

        dbContentType.ModifiedAt = DateTime.UtcNow;
        dbContentType.ModifiedBy = apiExecutionContext.Username;

        // Update the Fields collection
        var newFields = entity.Fields.Select(mapper.Map<DbModels.ContentTypeField>).ToList();

        // Handle additions and updates
        foreach (var newField in newFields)
        {
            newField.Id = Guid.NewGuid();
            newField.ContentTypeId = dbContentType.Id;
            dbContentType.Fields.Add(newField);
        }

        await dbContext.ContentTypeFields.AddRangeAsync(newFields, cancellationToken);

        // Mark the entity as modified
        dbContext.Entry(dbContentType).State = EntityState.Modified;

        await dbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<ContentType>(dbContentType);

    }
}
