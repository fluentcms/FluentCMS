using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Content.Query;
using FluentCMS.Publishing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Management;

// The REST forms of content as another surface gets them (spec 5.4): the same query and the same JSON as the routes.
public class ContentApiFormatTests
{
    private static readonly ContentApiFormat Format = new();

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

        var query = Format.ParseQuery(parameters);

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
        var refused = Should.Throw<ValidationException>(() => Format.ParseQuery([new("colour", "red")]));

        refused.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Path.ShouldBe("colour"),
            error => error.Code.ShouldBe(QueryErrorCodes.InvalidParameter));
    }

    [Fact]
    public void AnEntryAListAPublishResultAndAScheduleAreTheJsonOfTheRoutes()
    {
        var entry = new ContentDocument { ["id"] = Guid.CreateVersion7(), ["status"] = EntryStatus.Published, ["title"] = "Hello" };
        var page = new PagedResult<ContentDocument>([entry], 1, 1, 20, null);
        var published = new PublishResult<ContentDocument>(entry, [new PublishWarning("author", PublishWarningCodes.UnpublishedReference, "The author is not published.")]);
        var schedule = new ScheduleInfo(ScheduleAction.Publish, "en", new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero), null, 3);

        Format.Entry(entry).ToJsonString().ShouldBe(ContentJson.Entry(entry).ToJsonString());
        Format.Entry(entry)["status"]!.GetValue<string>().ShouldBe("published");
        Format.List(page).ToJsonString().ShouldBe(ContentJson.List(page).ToJsonString());
        Format.Published(published).ToJsonString().ShouldBe(ManagementJson.Published(published).ToJsonString());
        Format.Schedule(schedule)["runAt"]!.GetValue<string>().ShouldBe("2027-03-01T09:00:00Z");
    }

    [Fact]
    public void AScheduleBodyIsReadAsTheScheduleRouteReadsIt()
    {
        var instant = Format.ParseSchedule(Json("""{ "action": "publish", "at": "2027-03-01T09:00:00Z" }"""));
        var local = Format.ParseSchedule(Json("""{ "action": "unpublish", "at": "2027-03-01T09:00:00", "timeZone": "Europe/Berlin" }"""));

        instant.ShouldBe(ScheduleRequest.At(ScheduleAction.Publish, new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero)));
        local.ShouldBe(ScheduleRequest.AtLocalTime(ScheduleAction.Unpublish, new DateTime(2027, 3, 1, 9, 0, 0), "Europe/Berlin"));
        Should.Throw<ValidationException>(() => Format.ParseSchedule(Json("""{ "action": "publish", "at": "2027-03-01T09:00:00Z", "site": "x" }""")))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("site");
        Should.Throw<ValidationException>(() => Format.ParseSchedule(Json("[]"))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.InvalidValue);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
