namespace FluentCMS.Services;

public interface IBlockService : IAutoRegisterService
{
    Task<IEnumerable<Block>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Block> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Block> Create(Block Block, CancellationToken cancellationToken = default);
    Task<Block> Update(Block Block, CancellationToken cancellationToken = default);
    Task<Block> Delete(Guid id, CancellationToken cancellationToken = default);
}

public class BlockService(IBlockRepository blockRepository) : IBlockService
{
    public async Task<IEnumerable<Block>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await blockRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<Block> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await blockRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.BlockNotFound);
    }

    public async Task<Block> Create(Block block, CancellationToken cancellationToken = default)
    {
        var newBlock = await blockRepository.Create(block, cancellationToken) ??
            throw new AppException(ExceptionCodes.BlockUnableToCreate);
        return newBlock;
    }

    public async Task<Block> Update(Block block, CancellationToken cancellationToken = default)
    {
        var original = await blockRepository.GetById(block.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.BlockNotFound);

        original.Name = block.Name;
        original.Category = block.Category;
        original.Description = block.Description;
        original.Content = block.Content;

        return await blockRepository.Update(original, cancellationToken) ??
            throw new AppException(ExceptionCodes.BlockUnableToUpdate);
    }

    public async Task<Block> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await blockRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.BlockNotFound);

        return await blockRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.BlockUnableToDelete);
    }
}
