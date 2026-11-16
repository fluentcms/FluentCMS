using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Management;

// Results.Json gives options that are not read-only the reflection resolver when it is handed them, so a request that locks
// shared options between that check and the set answered 500 (#3193). First use happens once a process, so the race is run
// on options made as the shared ones are, a new set each round: half the threads hand them to Results.Json and half
// serialize with them, which locks them as a request that has got further does.
public sealed class AdminJsonTests
{
    private const int Rounds = 5000;
    private const int Threads = 8;

    private static readonly ItemList<Shape> Value = new([new Shape("Square", ShapeKind.FourSided)]);

    public static TheoryData<string> Shared => new() { "admin", "schema", "startup report" };

    [Theory]
    [MemberData(nameof(Shared))]
    public void SharedOptionsAreCompleteAndReadOnlyOnceTheTypeIsInitialized(string name)
    {
        var options = Options(name);

        options.IsReadOnly.ShouldBeTrue();
        options.TypeInfoResolver.ShouldNotBeNull();
    }

    [Fact]
    public void OptionsMadeReadOnlyAsTheSharedOnesTakeConcurrentFirstUses()
    {
        FirstUseFailures(() =>
        {
            var options = AsAdminJsonMakesThem();
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }).ShouldBe(0);
    }

    [Fact]
    public async Task TheReadOnlyOptionsWriteTheSameText()
    {
        (await WriteAsync(AdminJson.Options)).ShouldBe(await WriteAsync(AsAdminJsonMakesThem()));
    }

    private static JsonSerializerOptions Options(string name) => name switch
    {
        "admin" => AdminJson.Options,
        "schema" => (JsonSerializerOptions)typeof(SchemaEndpoints).GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!,
        _ => StartupReportJson.Options,
    };

    private static JsonSerializerOptions AsAdminJsonMakesThem() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static int FirstUseFailures(Func<JsonSerializerOptions> create)
    {
        var options = create();
        var failures = 0;
        using var round = new Barrier(Threads, _ => options = create());

        var threads = Enumerable.Range(0, Threads).Select(index => new Thread(() =>
        {
            for (var i = 0; i < Rounds; i++)
            {
                round.SignalAndWait();
                try
                {
                    if (index % 2 == 0)
                    {
                        Results.Json(Value, options);
                    }
                    else
                    {
                        JsonSerializer.Serialize(Value, options);
                    }
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        })).ToList();

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());
        return failures;
    }

    private static async Task<string> WriteAsync(JsonSerializerOptions options)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await Results.Json(Value, options).ExecuteAsync(context);

        return Encoding.UTF8.GetString(body.ToArray());
    }

    public sealed record Shape(string Name, ShapeKind Kind);

    public enum ShapeKind
    {
        FourSided,
    }
}
