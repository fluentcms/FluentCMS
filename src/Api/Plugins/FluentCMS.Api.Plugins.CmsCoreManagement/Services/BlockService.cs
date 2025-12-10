namespace FluentCMS.Api.Plugins.CmsCoreManagement.Services;

public interface IBlockService
{
    Task<IEnumerable<Block>> GetAll(CancellationToken cancellationToken = default);
    Task<IEnumerable<Block>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Block> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Block> Add(Block block, CancellationToken cancellationToken = default);
    Task<Block> Update(Block block, CancellationToken cancellationToken = default);
    Task<Block> Remove(Guid id, CancellationToken cancellationToken = default);
}

internal class BlockService(IBlockRepository blockRepository, IEventPublisher eventPublisher) : IBlockService
{
    public async Task<IEnumerable<Block>> GetAll(CancellationToken cancellationToken = default)
    {
        return await blockRepository.GetAll(cancellationToken);
    }

    public async Task<IEnumerable<Block>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await blockRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<Block> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await blockRepository.GetById(id, cancellationToken);
    }

    public async Task<Block> Add(Block block, CancellationToken cancellationToken = default)
    {

        await blockRepository.Add(block, cancellationToken);

        // await eventPublisher.Publish(new BlockAddedEvent(block), cancellationToken);

        return block;
    }

    public async Task<Block> Update(Block block, CancellationToken cancellationToken = default)
    {
        await blockRepository.Update(block, cancellationToken);

        // await eventPublisher.Publish(new BlockUpdatedEvent(block), cancellationToken);

        return block;
    }

    public async Task<Block> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var deletedBlock = await blockRepository.Remove(id, cancellationToken);

        // await eventPublisher.Publish(new BlockRemovedEvent(deletedBlock), cancellationToken);

        return deletedBlock;
    }
}
