using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Publishing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

// The REST forms of content as another surface gets them (spec 4.4, 5.4): the same query and the same JSON as the routes.
public class ContentSurfaceFormatTests
{
    private static readonly ContentSurface Surface = new(new SchemaBuilder().Build(), new ContentQueryOptions(), new ContentQueryAccess.Closed(), new OperationContextAccessor(), new ManagementShape(new OperationContextAccessor()), ErrorCodeCatalogue.Core);

    [Fact]
    public void TheParametersOfAListAreTheQueryTheRoutesParse()
    {
        KeyValuePair<string, string>[] parameters =
        [
            new("filter[category][eq]", "news"),
            new("filter[or][0][title][startsWith]", "A"),
            new("filter[or][1][rating][in]", "4,5"),
            new("sort", "-rating,title"),
            new("pageSize", "2"),
            new("status", "published"),
        ];

        var query = Surface.ParseQuery(parameters);

        var parsed = BracketQueryParser.Parse("filter[category][eq]=news&filter[or][0][title][startsWith]=A&filter[or][1][rating][in]=4,5&sort=-rating,title&pageSize=2&status=published");
        query.Filter.ShouldBe(parsed.Filter);
        query.Sort.ShouldBe([new SortField("rating", Descending: true), new SortField("title")]);
        query.Page.ShouldBe(new PageRequest { Size = 2 });
        query.Status.ShouldBe(QueryStatus.Published);
        query.Filter.ShouldBe(new And(
            new FieldFilter("category", FilterOperator.Equal, "news"),
            new Or(new FieldFilter("title", FilterOperator.StartsWith, "A"), new FieldFilter("rating", FilterOperator.In, new List<object?> { "4", "5" }))));
    }

    [Fact]
    public void AParameterOutsideTheGrammarIsRefusedWithItsName()
    {
        var refused = Should.Throw<ValidationException>(() => Surface.ParseQuery([new("colour", "red")]));

        refused.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Path.ShouldBe("colour"),
            error => error.Code.ShouldBe(QueryErrorCodes.InvalidParameter));
    }

    [Fact]
    public void AListAPublishResultAndAScheduleAreTheJsonOfTheRoutes()
    {
        var entry = new ContentDocument { ["id"] = Guid.CreateVersion7(), ["status"] = EntryStatus.Published, ["title"] = "Hello" };
        var page = new PagedResult<ContentDocument>([entry], 1, 1, 20, null);
        var published = new PublishResult<ContentDocument>(entry, [new PublishWarning("author", PublishWarningCodes.UnpublishedReference, "The author is not published.")]);
        var schedule = new ScheduleInfo(ScheduleAction.Publish, "en", new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero), null, 3);

        Surface.ToJson(page).ToJsonString().ShouldBe(ContentJson.List(page).ToJsonString());
        Surface.ToJson(page)["items"]![0]!["status"]!.GetValue<string>().ShouldBe("published");
        Surface.ToJson(published).ToJsonString().ShouldBe(ManagementJson.Published(published).ToJsonString());
        Surface.ToJson(published)["warnings"]![0]!["code"]!.GetValue<string>().ShouldBe(PublishWarningCodes.UnpublishedReference);
        Surface.ToJson(schedule)["runAt"]!.GetValue<string>().ShouldBe("2027-03-01T09:00:00Z");
        Surface.ToJson(schedule)["action"]!.GetValue<string>().ShouldBe("publish");
    }

    [Fact]
    public void ABulkResultIsWrittenAsTheBulkRouteWritesIt()
    {
        var id = Guid.NewGuid();
        var result = new BulkResult(
            [
                new BulkItemResult(0, id, BulkItemStatus.Succeeded) { Version = 2 },
                new BulkItemResult(1, Guid.NewGuid(), BulkItemStatus.Failed) { Problem = new BulkProblem("FCMS3104", "There is no article.") },
            ],
            Cancelled: false);

        var json = Surface.ToJson(result);

        json.ToJsonString().ShouldBe(ManagementJson.Bulk(result, authenticated: false).ToJsonString());
        json["items"]![0]!["status"]!.GetValue<string>().ShouldBe("succeeded");
        json["items"]![0]!["version"]!.GetValue<int>().ShouldBe(2);
        json["items"]![1]!["problem"]!["code"]!.GetValue<string>().ShouldBe("FCMS3104");
        json["cancelled"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void AScheduleBodyIsReadAsTheScheduleRouteReadsIt()
    {
        var instant = Surface.ParseSchedule(Json("""{ "action": "publish", "at": "2027-03-01T09:00:00Z" }"""));
        var local = Surface.ParseSchedule(Json("""{ "action": "unpublish", "at": "2027-03-01T09:00:00", "timeZone": "Europe/Berlin" }"""));

        instant.ShouldBe(ScheduleRequest.At(ScheduleAction.Publish, new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero)));
        local.ShouldBe(ScheduleRequest.AtLocalTime(ScheduleAction.Unpublish, new DateTime(2027, 3, 1, 9, 0, 0), "Europe/Berlin"));
        Should.Throw<ValidationException>(() => Surface.ParseSchedule(Json("""{ "action": "publish", "at": "2027-03-01T09:00:00Z", "site": "x" }""")))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("site");
        Should.Throw<ValidationException>(() => Surface.ParseSchedule(Json("[]"))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.InvalidValue);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
