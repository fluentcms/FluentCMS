namespace FluentCMS.Services;

public interface IHttpLogService : IAutoRegisterService
{
    void Log(HttpLog httpLog);
}

public class HttpLogService(IHttpLogChannel httpLogChannel) : IHttpLogService
{
    public void Log(HttpLog httpLog)
    {
        httpLogChannel.Write(httpLog);
    }
}
