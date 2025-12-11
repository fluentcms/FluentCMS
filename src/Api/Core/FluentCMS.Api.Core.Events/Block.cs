namespace FluentCMS.Api.Core.Events;

public class BlockAddedEvent(Block block) : EventBase
{
    public Block Block { get; } = block;
}

public class BlockUpdatedEvent(Block block) : EventBase
{
    public Block Block { get; } = block;
}

public class BlockRemovedEvent(Block block) : EventBase
{
    public Block Block { get; } = block;
}

public class BlocksRemovedBySiteEvent(IEnumerable<Block> blocks) : EventBase
{
    public IEnumerable<Block> Block { get; } = blocks;
}
