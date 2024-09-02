using System.IO;

namespace FluentCMS.Services.Setup.Handlers;

public class BlockHandler(IBlockService blockService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.Block;

    private string BlocksFolder = Path.Combine("Template", "Blocks");

    public async override Task<SetupContext> Handle(SetupContext context)
    {
        foreach (var blockTemplate in context.AdminTemplate.Blocks)
        {
            var block = new Block
            {
                Name = blockTemplate.Name,
                Category = blockTemplate.Category,
                Description = blockTemplate.Description,
                Content = System.IO.File.ReadAllText(Path.Combine(BlocksFolder, blockTemplate.Category, $"{blockTemplate.Name}.html")),
            };
            await blockService.Create(block);
        }

        return await base.Handle(context);
    }
}
