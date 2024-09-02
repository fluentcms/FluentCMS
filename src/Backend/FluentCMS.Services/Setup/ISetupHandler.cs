namespace FluentCMS.Services.Setup;

public interface ISetupHandler
{
    ISetupHandler SetNext(ISetupHandler handler);

    Task<SetupContext> Handle(SetupContext request);
}
