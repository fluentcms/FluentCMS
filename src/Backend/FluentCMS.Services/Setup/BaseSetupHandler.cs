namespace FluentCMS.Services.Setup;

public abstract class BaseSetupHandler : ISetupHandler
{
    private ISetupHandler _nextHandler;

    public ISetupHandler SetNext(ISetupHandler handler)
    {
        _nextHandler = handler;

        return handler;
    }

    public virtual async Task<SetupContext> Handle(SetupContext context)
    {
        if (this._nextHandler != null)
            return await this._nextHandler.Handle(context);

        return null;
    }

    public abstract SetupSteps Step { get; }
}
