using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

public sealed class PaginationTests : IDisposable
{
    private readonly QueryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private Guid Add(string title, long? views = null, decimal? price = null, bool? featured = null, DateTimeOffset? publishedOn = null, DateOnly? issued = null)
    {
        Dictionary<string, object?> values = [];
        void Set(string column, object? value)
        {
            if (value is not null)
            {
                values[column] = value;
            }
        }

        Set("views", views);
        Set("price", price);
        Set("featured", featured);
        Set("published_on", publishedOn);
        Set("issued", issued);
        return _database.AddArticle(title, values);
    }

    private static string[] Titles(ContentQueryResult result) => result.Items.Select(item => (string)item["title"]!).ToArray();

    private static Guid[] Ids(ContentQueryResult result) => result.Items.Select(item => (Guid)item["id"]!).ToArray();

    private Task<string[]> AllByCursor(string query) => AllByCursor(query, Titles);

    private async Task<T[]> AllByCursor<T>(string query, Func<ContentQueryResult, T[]> read)
    {
        List<T> seen = [];
        string? cursor = null;
        do
        {
            var result = await _database.RunAsync($"{query}&locale=en&limit=2{(cursor is null ? "" : $"&cursor={cursor}")}");
            result.Items.Count.ShouldBeLessThanOrEqualTo(2);
            seen.AddRange(read(result));
            cursor = result.NextCursor;
        }
        while (cursor is not null);

        return [.. seen];
    }

    [Fact]
    public async Task PagesByNumberWithATotal()
    {
        foreach (var title in "ABCDE")
        {
            Add(title.ToString());
        }

        var result = await _database.RunAsync("sort=title&page=2&pageSize=2&locale=en");

        Titles(result).ShouldBe(["C", "D"]);
        result.Total.ShouldBe(5);
        result.Page.ShouldBe(2);
        result.PageSize.ShouldBe(2);
        result.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task PagesByOffsetWithATotal()
    {
        foreach (var title in "ABC")
        {
            Add(title.ToString());
        }

        var result = await _database.RunAsync("sort=title&offset=1&limit=5&locale=en");

        Titles(result).ShouldBe(["B", "C"]);
        result.Total.ShouldBe(3);
        result.Page.ShouldBeNull();
        result.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task SkipsTheCountWhenAsked()
    {
        Add("A");

        (await _database.RunAsync("withTotal=false&locale=en")).Total.ShouldBeNull();
    }

    [Fact]
    public async Task ACursorPageIsNotCounted()
    {
        Add("A");
        Add("B");
        var first = await _database.RunAsync("limit=1&sort=title&locale=en");
        first.Total.ShouldBe(2);

        var next = await _database.RunAsync($"limit=1&sort=title&locale=en&cursor={first.NextCursor}");

        next.Total.ShouldBeNull();
        Titles(next).ShouldBe(["B"]);
        next.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task UsesTheDefaultPageSize()
    {
        for (var index = 0; index < 3; index++)
        {
            Add($"T{index}");
        }

        var result = await _database.RunAsync("locale=en", options: new ContentQueryOptions { DefaultPageSize = 2 });

        result.Items.Count.ShouldBe(2);
        result.PageSize.ShouldBe(2);
        result.Page.ShouldBe(1);
    }

    [Fact]
    public async Task SortsByManyFields()
    {
        Add("A", views: 2);
        Add("B", views: 1);
        Add("C", views: 2);

        Titles(await _database.RunAsync("sort=-views,title&locale=en")).ShouldBe(["A", "C", "B"]);
        Titles(await _database.RunAsync("sort=views,-title&locale=en")).ShouldBe(["B", "C", "A"]);
    }

    [Fact]
    public async Task AMissingValueSortsLowest()
    {
        Add("None");
        Add("Some", views: 1);

        Titles(await _database.RunAsync("sort=views&locale=en")).ShouldBe(["None", "Some"]);
        Titles(await _database.RunAsync("sort=-views&locale=en")).ShouldBe(["Some", "None"]);
    }

    [Fact]
    public async Task SortsBySystemFieldsAndComponentColumns()
    {
        var first = Add("A");
        var second = Add("B");

        (await _database.RunAsync("sort=-id&locale=en")).Items.Select(item => (Guid)item["id"]!).ShouldBe([second, first]);
        Titles(await _database.RunAsync("sort=seo.metaTitle,title&locale=en")).ShouldBe(["A", "B"]);
        Titles(await _database.RunAsync("sort=-createdAt,title&locale=en")).Length.ShouldBe(2);
    }

    [Fact]
    public async Task EqualKeysKeepAStableOrderThroughTheDocumentId()
    {
        var ids = new[] { Add("Same"), Add("Same"), Add("Same") };

        // The order of the ids is the provider's: SQL Server sorts uniqueidentifier by its last bytes first, not as text.
        var byId = Ids(await _database.RunAsync("sort=id&locale=en"));

        byId.ShouldBe(ids, ignoreOrder: true);
        Ids(await _database.RunAsync("sort=title&locale=en")).ShouldBe(byId);
        (await AllByCursor("sort=title", Ids)).ShouldBe(byId);
    }

    [Theory]
    [InlineData("sort=views")]
    [InlineData("sort=-views")]
    [InlineData("sort=price")]
    [InlineData("sort=-price")]
    [InlineData("sort=featured")]
    [InlineData("sort=-featured")]
    [InlineData("sort=publishedOn")]
    [InlineData("sort=-publishedOn")]
    [InlineData("sort=issued")]
    [InlineData("sort=-issued")]
    [InlineData("sort=title")]
    [InlineData("sort=-title")]
    [InlineData("sort=-id")]
    [InlineData("sort=-views,title")]
    [InlineData("sort=featured,-price,title")]
    public async Task ACursorWalksEveryEntryOnceInTheSameOrderAsAnOffset(string sort)
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var day = new DateOnly(2026, 1, 1);
        var values = new (string Title, long? Views, decimal? Price, bool? Featured, int? Day)[]
        {
            ("A", 1, 1.5m, true, 1),
            ("B", 1, 10.25m, false, 2),
            ("C", null, null, null, null),
            ("D", 2, 2m, true, 1),
            ("E", null, 3m, false, null),
            ("F", 3, null, null, 3),
            ("G", 2, 2m, true, 2),
        };
        foreach (var (title, views, price, featured, offset) in values)
        {
            Add(title, views, price, featured, offset is { } days ? start.AddDays(days) : null, offset is { } date ? day.AddDays(date) : null);
        }

        var byOffset = Titles(await _database.RunAsync($"{sort}&locale=en&limit=100"));

        byOffset.Length.ShouldBe(values.Length);
        (await AllByCursor(sort)).ShouldBe(byOffset);
    }

    [Fact]
    public async Task ACursorIsStableAcrossAnInsertBeforeAndAfterItsPosition()
    {
        foreach (var title in "BDFH")
        {
            Add(title.ToString());
        }

        var first = await _database.RunAsync("sort=title&limit=2&locale=en");
        Titles(first).ShouldBe(["B", "D"]);

        Add("A");
        Add("E");

        var second = await _database.RunAsync($"sort=title&limit=2&locale=en&cursor={first.NextCursor}");
        Titles(second).ShouldBe(["E", "F"]);

        var third = await _database.RunAsync($"sort=title&limit=2&locale=en&cursor={second.NextCursor}");
        Titles(third).ShouldBe(["H"]);
        third.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ACursorIsStableAcrossDeletesIncludingTheRowItPointsAt()
    {
        var ids = "ABCDE".ToDictionary(title => title.ToString(), title => Add(title.ToString()));

        var first = await _database.RunAsync("sort=title&limit=2&locale=en");
        Titles(first).ShouldBe(["A", "B"]);

        _database.Delete(ids["B"]);
        _database.Delete(ids["C"]);
        _database.Trash(ids["A"]);

        var second = await _database.RunAsync($"sort=title&limit=2&locale=en&cursor={first.NextCursor}");
        Titles(second).ShouldBe(["D", "E"]);
        second.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ACursorDoesNotLoseTiedRows()
    {
        foreach (var index in Enumerable.Range(0, 5))
        {
            Add($"T{index}", views: 1);
        }

        (await AllByCursor("sort=views")).Length.ShouldBe(5);
    }

    [Fact]
    public async Task ACursorIsBase64OfTheSortKeysAndTheDocumentId()
    {
        var a = Add("A", views: 1);
        Add("B", views: 2);

        var result = await _database.RunAsync("sort=-views,title&limit=1&locale=en");
        var json = System.Text.Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(result.NextCursor!));
        var expected = (await _database.RunAsync("sort=-views,title&limit=1&locale=en")).Items.Single()["id"];

        json.ShouldBe($$"""{"-views":"2","title":"B","id":"{{expected}}"}""");
        expected.ShouldNotBe(a);
    }
}
