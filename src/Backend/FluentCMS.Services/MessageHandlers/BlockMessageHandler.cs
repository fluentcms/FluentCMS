namespace FluentCMS.Services.MessageHandlers;

public class BlockMessageHandler(IBlockService blockService) : IMessageHandler<SiteTemplate>, IMessageHandler<Site>
{
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                foreach (var block in notification.Payload.Blocks)
                {
                    block.SiteId = notification.Payload.Id;
                    await blockService.Create(block, cancellationToken);
                }
                break;

            default:
                break;
        }
    }

    public async Task Handle(Message<Site> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.SiteDeleted:
                var siteId = notification.Payload.Id;
                var blockIds = (await blockService.GetAllForSite(siteId, cancellationToken)).Select(b => b.Id);
                await blockService.Delete(blockIds, cancellationToken);
                break;

            default:
                break;
        }
    }
}
