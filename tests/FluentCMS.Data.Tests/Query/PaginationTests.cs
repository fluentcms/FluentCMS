using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Data.Tests.Providers;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

public sealed class PaginationTests : IDisposable
{
    // In code point order, which a language's collation changes for case, punctuation and accents.
    private static readonly string[] ByCodePoint = ["Banana", "_cherry", "apple", "zebra", "Éclair"];

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

        // In the provider's order of ids, which on SQL Server is not the order the v7 ids were made in.
        var byId = Ids(await _database.RunAsync("sort=id&locale=en"));
        byId.ShouldBe([first, second], ignoreOrder: true);
        Ids(await _database.RunAsync("sort=-id&locale=en")).ShouldBe(byId.AsEnumerable().Reverse());
        Titles(await _database.RunAsync("sort=seo.metaTitle,title&locale=en")).ShouldBe(["A", "B"]);
        Titles(await _database.RunAsync("sort=-createdAt,title&locale=en")).Length.ShouldBe(2);
    }

    [Fact]
    public async Task TextSortsByCodePointOnEveryProvider()
    {
        foreach (var title in ByCodePoint.AsEnumerable().Reverse())
        {
            Add(title);
        }

        Titles(await _database.RunAsync("sort=title&locale=en")).ShouldBe(ByCodePoint);
        Titles(await _database.RunAsync("sort=-title&locale=en")).ShouldBe(ByCodePoint.AsEnumerable().Reverse());
    }

    // A cursor continues a text key with > and <, which compare as the sort does.
    [Fact]
    public async Task ACursorComparesTextByCodePointOnEveryProvider()
    {
        foreach (var title in ByCodePoint)
        {
            Add(title);
        }

        (await AllByCursor("sort=title")).ShouldBe(ByCodePoint);
        (await AllByCursor("sort=-title")).ShouldBe(ByCodePoint.AsEnumerable().Reverse());
    }

    // A character at U+10000 or above is a surrogate pair in UTF-16, whose first unit is in D800 to DBFF. SQLite and
    // PostgreSQL's C compare UTF-8 bytes, which is code point order, and SQL Server's Latin1_General_100_BIN2 compares the
    // UTF-16 units of nvarchar, so there the pair sorts before U+E000 to U+FFFF (#3476). The order is the provider's own in
    // the sort and the cursor alike. On a failure the message gives the order the database gave.
    [Fact]
    public async Task TextAboveUFFFFSortsByCodePointExceptByUtf16UnitOnSqlServer()
    {
        const string Low = "z";
        const string High = "\uFF21";
        const string Astral = "\U0001F600";
        const string LowThenHigh = Low + High;
        const string LowThenAstral = Low + Astral;
        string[] byCodePoint = [Low, LowThenHigh, LowThenAstral, High, Astral];
        string[] byUtf16Unit = [Low, LowThenAstral, LowThenHigh, Astral, High];
        var expected = TestProvider.IsSqlServer ? byUtf16Unit : byCodePoint;
        foreach (var title in byCodePoint.AsEnumerable().Reverse())
        {
            Add(title);
        }

        void ShouldBeInOrder(string[] actual, IEnumerable<string> wanted, string what) =>
            Points(actual).ShouldBe(Points(wanted), $"{TestProvider.Name} gave {what} as {Points(actual)}, by code point {Points(byCodePoint)}, by UTF-16 unit {Points(byUtf16Unit)}");

        ShouldBeInOrder(Titles(await _database.RunAsync("sort=title&locale=en")), expected, "sort=title");
        ShouldBeInOrder(Titles(await _database.RunAsync("sort=-title&locale=en")), expected.AsEnumerable().Reverse(), "sort=-title");
        ShouldBeInOrder(await AllByCursor("sort=title"), expected, "the cursor pages of sort=title");
        ShouldBeInOrder(await AllByCursor("sort=-title"), expected.AsEnumerable().Reverse(), "the cursor pages of sort=-title");

        // Text has no lt or gt filter; a cursor that ends on U+FF21 asks for what is greater, or less in a descending sort.
        ShouldBeInOrder(await AfterAsync("sort=title", expected, High), [.. expected.SkipWhile(title => title != High).Skip(1)], "the titles after U+FF21 by sort=title");
        ShouldBeInOrder(await AfterAsync("sort=-title", [.. expected.AsEnumerable().Reverse()], High), [.. expected.TakeWhile(title => title != High).Reverse()], "the titles after U+FF21 by sort=-title");
    }

    // The entries after one, from the cursor of a page that ends on it.
    private async Task<string[]> AfterAsync(string sort, string[] order, string title)
    {
        var page = await _database.RunAsync($"{sort}&limit={Array.IndexOf(order, title) + 1}&locale=en");
        Titles(page)[^1].ShouldBe(title);
        return page.NextCursor is null ? [] : Titles(await _database.RunAsync($"{sort}&limit=100&locale=en&cursor={page.NextCursor}"));
    }

    private static string Points(IEnumerable<string> titles) =>
        string.Join(" | ", titles.Select(title => string.Join(' ', title.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"))));

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
