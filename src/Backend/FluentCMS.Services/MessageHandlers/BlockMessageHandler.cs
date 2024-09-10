using FluentCMS.Providers.MessageBusProviders;
using System.IO;

namespace FluentCMS.Services.MessageHandlers;

public class BlockMessageHandler(IBlockService blockService) : IMessageHandler<SiteTemplate>
{    
    public async Task Handle(Message<SiteTemplate> notification, CancellationToken cancellationToken)
    {
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine("HANDLE");
        Console.WriteLine($"HANDLE {notification.Payload.Blocks.Count}");
        var BlocksFolder = Path.Combine("Template", "Blocks");

        switch (notification.Action)
        {
            case ActionNames.SiteCreated:
                foreach (var block in notification.Payload.Blocks)
                {
                    block.SiteId = notification.Payload.Id;
                    await blockService.Create(block);
                }
                break;

            default:
                break;
        }
    }
}