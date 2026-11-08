using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

// A fake of IContentService<T> implements the members that map an entry; the others reach the document service it names.
public sealed class TypedContentServiceDefaultsTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task TheMembersThatMapNoEntryGoThroughTheDocumentServiceWithTheTypeName()
    {
        var documents = DispatchProxy.Create<IContentService, Recorder>();
        IContentService<Post> posts = new PostService(documents);
        var locale = new LocaleOptions { Locale = "fr" };
        var schedule = ScheduleRequest.At(ScheduleAction.Publish, DateTimeOffset.UnixEpoch);
        var count = new CountOptions { Status = QueryStatus.Draft };
        var bulk = new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(Id)] };
        var token = TestContext.Current.CancellationToken;

        await posts.DeleteAsync(Id, token);
        await posts.PurgeAsync(Id, token);
        await posts.ScheduleAsync(Id, schedule, locale, token);
        await posts.CancelScheduleAsync(Id, ScheduleAction.Unpublish, locale, token);
        await posts.GetVersionsAsync(Id, locale, token);
        await posts.DiffAsync(Id, 1, 2, locale, token);
        await posts.CountAsync(count, token);
        await posts.BulkAsync(bulk, token);

        (string Name, object?[] Arguments)[] expected =
        [
            ("DeleteAsync", ["post", Id, token]),
            ("PurgeAsync", ["post", Id, token]),
            ("ScheduleAsync", ["post", Id, schedule, locale, token]),
            ("CancelScheduleAsync", ["post", Id, ScheduleAction.Unpublish, locale, token]),
            ("GetVersionsAsync", ["post", Id, locale, token]),
            ("DiffAsync", ["post", Id, 1, 2, locale, token]),
            ("CountAsync", ["post", count, token]),
            ("BulkAsync", ["post", bulk, token]),
        ];
        var calls = ((Recorder)(object)documents).Calls;
        calls.Select(call => call.Name).ShouldBe(expected.Select(call => call.Name));
        for (var index = 0; index < expected.Length; index++)
        {
            calls[index].Arguments.ShouldBe(expected[index].Arguments);
        }
    }

    public sealed class Post : ContentItem;

    public class Recorder : DispatchProxy
    {
        public List<(string Name, object?[] Arguments)> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            Calls.Add((targetMethod.Name, args ?? []));

            var type = targetMethod.ReturnType;
            if (!type.IsGenericType)
            {
                return Task.CompletedTask;
            }

            var result = type.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result)
                .Invoke(null, [result.IsValueType ? Activator.CreateInstance(result) : null]);
        }
    }

    private sealed class PostService(IContentService documents) : IContentService<Post>
    {
        public string Type => "post";

        public IContentService Documents => documents;

        public Task<Post?> GetAsync(Guid id, GetOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PagedResult<Post>> QueryAsync(ContentQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post> CreateAsync(Post entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post> UpdateAsync(Guid id, Post entry, int expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post> PatchAsync(Guid id, ContentDocument patch, int expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post> RestoreAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PagedResult<Post>> QueryTrashAsync(ContentQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PublishResult<Post>> PublishAsync(Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post> UnpublishAsync(Guid id, LocaleOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post> DiscardDraftAsync(Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post?> GetVersionAsync(Guid id, int number, LocaleOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Post> RestoreVersionAsync(Guid id, int number, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
