using AutoMapper;
using AutoMapper.QueryableExtensions;

namespace FluentCMS.Repositories.EFCore;

public class ApiTokenRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext, IMapper mapper) : AuditableEntityRepository<ApiToken>(dbContext, apiExecutionContext), IApiTokenRepository
{
    public override async Task<ApiToken?> Create(ApiToken entity, CancellationToken cancellationToken = default)
    {
        var dbEntity = mapper.Map<DbModels.ApiToken>(entity);

        dbEntity.Id = Guid.NewGuid();
        dbEntity.CreatedBy = ApiExecutionContext.Username;
        dbEntity.CreatedAt = DateTime.UtcNow;

        await DbContext.ApiTokens.AddAsync(dbEntity, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<ApiToken>(dbEntity);
    }

    public override async Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default)
    {
        return await DbContext.ApiTokens.ProjectTo<ApiToken>(mapper.ConfigurationProvider).ToListAsync(cancellationToken);
    }

    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.ApiTokens.FirstOrDefaultAsync(x => x.Key == apiKey, cancellationToken);
        return mapper.Map<ApiToken>(dbEntity);
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.ApiTokens.FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
        return mapper.Map<ApiToken>(dbEntity);
    }

    public override async Task<IEnumerable<ApiToken>> CreateMany(IEnumerable<ApiToken> entities, CancellationToken cancellationToken = default)
    {
        var dbEntities = mapper.Map<List<DbModels.ApiToken>>(entities);

        foreach (var item in entities)
            SetAuditableFieldsForCreate(item);

        await DbContext.ApiTokens.AddRangeAsync(dbEntities, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<IEnumerable<ApiToken>>(dbEntities);

    }

    public override async Task<ApiToken?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.ApiTokens.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dbEntity == null)
            return null;

        DbContext.ApiTokens.Remove(dbEntity);
        await DbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<ApiToken>(dbEntity);
    }

    public override async Task<IEnumerable<ApiToken>> DeleteMany(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.ApiTokens.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (dbEntities.Count == 0)
            return [];

        DbContext.ApiTokens.RemoveRange(dbEntities);
        await DbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<IEnumerable<ApiToken>>(dbEntities);
    }

    public override async Task<ApiToken?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.ApiTokens.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return mapper.Map<ApiToken>(dbEntity);
    }

    public override async Task<IEnumerable<ApiToken>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.ApiTokens.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        return mapper.Map<IEnumerable<ApiToken>>(dbEntities);
    }

    public override async Task<ApiToken?> Update(ApiToken entity, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.ApiTokens.FirstOrDefaultAsync(x => x.Id == entity.Id, cancellationToken);
        if (dbEntity == null)
            return null;

        mapper.Map(entity, dbEntity); // Update dbEntity with the new data

        // these fields should be updated
        dbEntity.ModifiedAt = DateTime.UtcNow;
        dbEntity.ModifiedBy = ApiExecutionContext.Username;

        DbContext.ApiTokens.Update(dbEntity);
        await DbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<ApiToken>(dbEntity);
    }
}
