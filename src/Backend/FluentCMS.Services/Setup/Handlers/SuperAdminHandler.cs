namespace FluentCMS.Services.Setup.Handlers;

public class SuperAdminHandler(IUserService userService) : BaseSetupHandler
{
    public override SetupSteps Step => SetupSteps.SuperAdmin;

    public override async Task<SetupContext> Handle(SetupContext context)
    {
        var superAdmin = new User
        {
            UserName = context.SetupRequest.Username,
            Email = context.SetupRequest.Email,
        };

        context.SuperAdmin = await userService.Create(superAdmin, context.SetupRequest.Password);

        return await base.Handle(context);
    }
}
