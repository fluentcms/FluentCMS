using System.IO;

namespace FluentCMS.Services.Setup.Handlers;

public class LayoutHandler(ILayoutService layoutService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.Layout;

    private const string ADMIN_TEMPLATE_PHYSICAL_PATH = "Template";

    public async override Task<SetupContext> Handle(SetupContext context)
    {
        foreach (var layoutTemplate in context.AdminTemplate.Layouts)
        {
            var layout = new Layout
            {
                Name = layoutTemplate.Name,
                Body = System.IO.File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, $"{layoutTemplate.Name}.body.html")),
                Head = System.IO.File.ReadAllText(Path.Combine(ADMIN_TEMPLATE_PHYSICAL_PATH, $"{layoutTemplate.Name}.head.html"))
            };
            context.Layouts.Add(await layoutService.Create(layout));
            if (layoutTemplate.IsDefault)
                context.DefaultLayoutId = layout.Id;
        }

        return await base.Handle(context);
    }
}
