using FluentCMS.Admin.Audit;
using FluentCMS.Core.Audit;

namespace FluentCMS.Tests.Admin;

public sealed class AuditExportFormTests
{
    [Fact]
    public void Dates_are_utc_days_with_both_ends_included()
    {
        var request = new AuditExportForm { From = "2026-10-01", To = "2026-10-31", Format = "csv" }.ToRequest();

        Assert.True(request.IsSuccess);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), request.Value.Query.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), request.Value.Query.To);
        Assert.Null(request.Value.Query.Subject);
        Assert.Equal(AuditExportFormat.Csv, request.Value.Format);
    }

    [Fact]
    public void Empty_fields_select_the_full_log_and_one_day_is_a_valid_window()
    {
        var full = new AuditExportForm { From = " ", To = "", Format = "JSON" }.ToRequest();
        var oneDay = new AuditExportForm { From = "2026-10-01", To = "2026-10-01", Format = "json" }.ToRequest();

        Assert.Equal(new AuditQuery(), full.Value.Query);
        Assert.Equal(AuditExportFormat.Json, full.Value.Format);
        Assert.Equal(TimeSpan.FromDays(1), oneDay.Value.Query.To - oneDay.Value.Query.From);
    }

    [Fact]
    public void An_item_type_and_id_select_that_items_history()
    {
        var id = Guid.NewGuid();

        var request = new AuditExportForm { SubjectType = " entry ", SubjectId = $" {id} ", Format = "json" }.ToRequest();

        Assert.Equal(new AuditSubject("entry", id), request.Value.Query.Subject);
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var request = new AuditExportForm
        {
            From = "2026-10-02",
            To = "2026-10-01",
            SubjectType = "entry",
            SubjectId = "not-an-id",
            Format = "xml",
        }.ToRequest();

        Assert.Equal(
            [AuditErrorCodes.WindowInverted, AuditExportForm.SubjectIdInvalid, AuditErrorCodes.ExportFormatUnknown],
            request.Errors.Select(error => error.Code));
    }

    [Theory]
    [InlineData("10/01/2026", null, AuditExportForm.FromInvalid)]
    [InlineData(null, "2026-13-01", AuditExportForm.ToInvalid)]
    public void An_unreadable_date_is_reported_for_its_field(string? from, string? to, string code)
    {
        var request = new AuditExportForm { From = from, To = to, Format = "json" }.ToRequest();

        Assert.Equal(code, Assert.Single(request.Errors).Code);
    }

    [Theory]
    [InlineData("entry", "", AuditErrorCodes.SubjectIdMissing)]
    [InlineData("", "3f2504e0-4f89-41d3-9a0c-0305e82c3301", AuditErrorCodes.SubjectTypeMissing)]
    [InlineData("entry", "00000000-0000-0000-0000-000000000000", AuditErrorCodes.SubjectIdMissing)]
    public void Half_an_item_is_reported_for_the_missing_half(string type, string id, string code)
    {
        var request = new AuditExportForm { SubjectType = type, SubjectId = id, Format = "json" }.ToRequest();

        Assert.Equal(code, Assert.Single(request.Errors).Code);
    }

    [Fact]
    public void A_rejected_form_returns_its_values_and_each_error_code_once()
    {
        var form = new AuditExportForm { From = "2026-10-02", To = "2026-10-01", SubjectType = "", SubjectId = "x y", Format = "json" };

        var query = form.ReturnQuery(form.ToRequest().Errors).ToList();

        Assert.Equal(
            [
                KeyValuePair.Create("from", (string?)"2026-10-02"),
                KeyValuePair.Create("to", (string?)"2026-10-01"),
                KeyValuePair.Create("subjectId", (string?)"x y"),
                KeyValuePair.Create("error", (string?)AuditErrorCodes.WindowInverted),
                KeyValuePair.Create("error", (string?)AuditErrorCodes.SubjectTypeMissing),
                KeyValuePair.Create("error", (string?)AuditExportForm.SubjectIdInvalid),
            ],
            query);
    }
}
