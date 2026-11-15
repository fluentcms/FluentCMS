using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Server = FluentCMS.Content;

namespace FluentCMS.Client.Tests;

// What the Delivery API hands the content service, recorded below it, so that a test sees how the server read a query
// string and not only that it answered.
public sealed class ServerQueries
{
    private static readonly Dictionary<string, Server.FilterOperator> Calls = new(StringComparer.Ordinal)
    {
        ["eq"] = Server.FilterOperator.Equal,
        ["ne"] = Server.FilterOperator.NotEqual,
        ["in"] = Server.FilterOperator.In,
        ["notIn"] = Server.FilterOperator.NotIn,
        ["lt"] = Server.FilterOperator.LessThan,
        ["lte"] = Server.FilterOperator.LessThanOrEqual,
        ["gt"] = Server.FilterOperator.GreaterThan,
        ["gte"] = Server.FilterOperator.GreaterThanOrEqual,
        ["between"] = Server.FilterOperator.Between,
        ["contains"] = Server.FilterOperator.Contains,
        ["startsWith"] = Server.FilterOperator.StartsWith,
        ["isNull"] = Server.FilterOperator.IsNull,
        ["isNotNull"] = Server.FilterOperator.IsNotNull,
        ["hasBlock"] = Server.FilterOperator.HasBlock,
    };

    private readonly ConcurrentQueue<(string Type, Server.ContentQuery Query)> _queries = new();

    public void Clear() => _queries.Clear();

    public IReadOnlyList<Server.ContentQuery> Of(string type) => [.. _queries.Where(query => query.Type == type).Select(query => query.Query)];

    // The host's content service, wrapped so that every query it is given is recorded. The service is core's own type, which
    // this project does not see, so it is found by its name.
    public void AddTo(IServiceCollection services) =>
        services.AddScoped(provider => Recorder.Wrap(
            (Server.IContentService)provider.GetRequiredService(Type.GetType("FluentCMS.Core.Content.ContentService, FluentCMS.Core", throwOnError: true)!),
            this));

    // A case of clients/filter-cases.json as the server's parser reads its query string: the path, the operator its call
    // names, and the text of each argument, read from the case and not from what a client wrote.
    public static Server.ContentFilter Filter(JsonElement node)
    {
        if (node.TryGetProperty("and", out var every))
        {
            return new Server.And([.. every.EnumerateArray().Select(Filter)]);
        }

        if (node.TryGetProperty("or", out var any))
        {
            return new Server.Or([.. any.EnumerateArray().Select(Filter)]);
        }

        if (node.TryGetProperty("not", out var negated))
        {
            return new Server.Not(Filter(negated));
        }

        var path = node.GetProperty("path").GetString()!;
        if (node.TryGetProperty("key", out var key))
        {
            path += "." + key.GetString();
        }

        var filterOperator = Calls[node.GetProperty("call").GetString()!];
        List<object?> args = [.. node.GetProperty("args").EnumerateArray().Select(arg => (object?)(arg.ValueKind == JsonValueKind.String ? arg.GetString() : arg.GetRawText()))];
        return new Server.FieldFilter(path, filterOperator, filterOperator switch
        {
            Server.FilterOperator.In or Server.FilterOperator.NotIn or Server.FilterOperator.Between => args,
            Server.FilterOperator.IsNull or Server.FilterOperator.IsNotNull => null,
            _ => args.Single(),
        });
    }

    public static Server.SortField Sort(JsonElement node) => new(node.GetProperty("path").GetString()!, node.GetProperty("call").GetString() == "descending");

    public class Recorder : DispatchProxy
    {
        private Server.IContentService _inner = null!;
        private ServerQueries _queries = null!;

        public static Server.IContentService Wrap(Server.IContentService inner, ServerQueries queries)
        {
            var proxy = Create<Server.IContentService, Recorder>();
            var recorder = (Recorder)(object)proxy;
            recorder._inner = inner;
            recorder._queries = queries;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(Server.IContentService.QueryAsync) && args is [string type, Server.ContentQuery query, ..])
            {
                _queries._queries.Enqueue((type, query));
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Throw(inner);
                throw;
            }
        }
    }
}
