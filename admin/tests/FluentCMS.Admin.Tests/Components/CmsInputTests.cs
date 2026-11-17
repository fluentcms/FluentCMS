using System.Globalization;
using Bunit;
using FluentCMS.Admin.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed class CmsInputTests : ComponentTestContext
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void TheLabelNamesTheInput()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title"));

        var label = input.Find("label");
        label.TextContent.Trim().ShouldBe("Title");
        label.GetAttribute("for").ShouldBe(input.Find("input").Id);
    }

    [Fact]
    public void HelpDescribesTheInput()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.Help, "Shown in lists."));

        var describedBy = input.Find("input").GetAttribute("aria-describedby");
        describedBy.ShouldNotBeNull();
        input.Find($"#{describedBy}").TextContent.ShouldBe("Shown in lists.");
        input.Find("input").HasAttribute("aria-invalid").ShouldBeFalse();
    }

    [Fact]
    public void AnErrorMarksTheInputInvalidAndReplacesTheHelp()
    {
        var input = Render<CmsTextInput>(p => p
            .Add(x => x.Label, "Slug")
            .Add(x => x.Help, "Used in the URL.")
            .Add(x => x.Error, "A slug is required."));

        var element = input.Find("input");
        element.GetAttribute("aria-invalid").ShouldBe("true");
        var message = input.Find($"#{element.GetAttribute("aria-describedby")}");
        message.TextContent.ShouldBe("A slug is required.");
        message.ClassList.ShouldContain("cms-field-message-error");
        input.Markup.ShouldNotContain("Used in the URL.");
    }

    [Fact]
    public void AnInputWithoutHelpOrErrorIsDescribedByNothing()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Email"));

        input.Find("input").HasAttribute("aria-describedby").ShouldBeFalse();
        input.FindAll(".cms-field-message").ShouldBeEmpty();
    }

    [Fact]
    public void ARequiredInputSaysSoToAssistiveTechnologyAndShowsAMarkerHiddenFromIt()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.Required, true));

        input.Find("input").GetAttribute("aria-required").ShouldBe("true");
        input.Find(".cms-field-required").GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void TypingRaisesValueChanged()
    {
        string? value = null;
        var input = Render<CmsTextInput>(p => p
            .Add(x => x.Label, "Title")
            .Add(x => x.ValueChanged, v => value = v));

        input.Find("input").Input("Hello");

        value.ShouldBe("Hello");
    }

    [Fact]
    public void TheTypeSetsTheInputType()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Email").Add(x => x.Type, CmsTextInputType.Email));

        input.Find("input").GetAttribute("type").ShouldBe("email");
    }

    [Fact]
    public void AOneTimeCodeAsksForTheNumericKeyboardAndTheBrowsersCode()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Code").Add(x => x.Autocomplete, "one-time-code").Add(x => x.Numeric, true));

        input.Find("input").GetAttribute("autocomplete").ShouldBe("one-time-code");
        input.Find("input").GetAttribute("inputmode").ShouldBe("numeric");
        input.Find("input").GetAttribute("type").ShouldBe("text");
    }

    [Fact]
    public void WithoutThemTheInputHasNeitherAttribute()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title"));

        input.Find("input").HasAttribute("autocomplete").ShouldBeFalse();
        input.Find("input").HasAttribute("inputmode").ShouldBeFalse();
    }

    [Fact]
    public void AGivenIdIsUsed()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.Id, "title").Add(x => x.Help, "Help."));

        input.Find("input").Id.ShouldBe("title");
        input.Find("input").GetAttribute("aria-describedby").ShouldBe("title-message");
    }

    [Fact]
    public void TheTextAreaIsLabelledAndDescribed()
    {
        var area = Render<CmsTextArea>(p => p.Add(x => x.Label, "Summary").Add(x => x.Error, "Too long."));

        var element = area.Find("textarea");
        area.Find("label").GetAttribute("for").ShouldBe(element.Id);
        element.GetAttribute("aria-invalid").ShouldBe("true");
        area.Find($"#{element.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Too long.");
    }

    [Fact]
    public void TheSelectIsLabelledDescribedAndRaisesTheChosenValue()
    {
        string? value = "draft";
        var select = Render<CmsSelect>(p => p
            .Add(x => x.Label, "Status")
            .Add(x => x.Help, "Who can see it.")
            .Add(x => x.Required, true)
            .Add(x => x.Options, [new CmsOption("draft", "Draft"), new CmsOption("published", "Published")])
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, v => value = v));

        var element = select.Find("select");
        select.Find("label").GetAttribute("for").ShouldBe(element.Id);
        element.GetAttribute("aria-required").ShouldBe("true");
        select.Find($"#{element.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Who can see it.");
        select.FindAll("option").Select(o => o.TextContent).ShouldBe(["Draft", "Published"]);

        element.Change("published");

        value.ShouldBe("published");
    }

    [Fact]
    public void TheSelectOffersAnEmptyOptionWhileNothingIsChosen()
    {
        var select = Render<CmsSelect>(p => p
            .Add(x => x.Label, "Status")
            .Add(x => x.Options, [new CmsOption("draft", "Draft")]));

        select.FindAll("option")[0].GetAttribute("value").ShouldBe(string.Empty);
    }

    [Fact]
    public void AValueOutsideTheOptionsLeavesTheEmptyOptionSelected()
    {
        var select = Render<CmsSelect>(p => p
            .Add(x => x.Label, "Status")
            .Add(x => x.Options, [new CmsOption("draft", "Draft")])
            .Add(x => x.Value, "archived"));

        select.FindAll("option")[0].GetAttribute("value").ShouldBe(string.Empty);
        select.FindAll("option[selected]").ShouldBeEmpty();
    }

    [Fact]
    public void TheDatePickerIsLabelledDescribedAndReadsTheDateInTheCultureOfTheUser()
    {
        DateOnly? value = null;
        var picker = Render<CmsDatePicker>(p => p
            .Add(x => x.Label, "Publish date")
            .Add(x => x.Error, "Pick a date.")
            .Add(x => x.Culture, German)
            .Add(x => x.ValueChanged, v => value = v));

        var element = picker.Find("input");
        element.GetAttribute("type").ShouldBe("text");
        element.GetAttribute("placeholder").ShouldBe("31.12.2026");
        picker.Find("label").GetAttribute("for").ShouldBe(element.Id);
        element.GetAttribute("aria-invalid").ShouldBe("true");
        picker.Find($"#{element.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Pick a date.");

        element.Change("15.11.2026");

        value.ShouldBe(new DateOnly(2026, 11, 15));
    }

    [Theory]
    [InlineData("en-US", "1/2/2026")]
    [InlineData("de-DE", "02.01.2026")]
    [InlineData("ja-JP", "2026/01/02")]
    public void TheDatePickerShowsItsValueInTheCultureNotTheBrowsers(string culture, string shown)
    {
        var picker = Render<CmsDatePicker>(p => p
            .Add(x => x.Label, "Publish date")
            .Add(x => x.Culture, CultureInfo.GetCultureInfo(culture))
            .Add(x => x.Value, new DateOnly(2026, 1, 2)));

        picker.Find("input").GetAttribute("value").ShouldBe(shown);
    }

    [Fact]
    public void TheDatePickerReadsTheIsoFormTooAndSaysWhatItCannotRead()
    {
        DateOnly? value = null;
        var raised = 0;
        var picker = Render<CmsDatePicker>(p => p
            .Add(x => x.Label, "Publish date")
            .Add(x => x.Culture, German)
            .Add(x => x.ValueChanged, v => { value = v; raised++; }));

        picker.Find("input").Change("2026-11-15");
        value.ShouldBe(new DateOnly(2026, 11, 15));

        picker.Find("input").Change("next week");

        raised.ShouldBe(1);
        var element = picker.Find("input");
        element.GetAttribute("aria-invalid").ShouldBe("true");
        picker.Find($"#{element.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Enter a date such as 31.12.2026.");
    }

    [Fact]
    public void ADateOutsideTheRangeIsNotTaken()
    {
        var raised = false;
        var picker = Render<CmsDatePicker>(p => p
            .Add(x => x.Label, "Publish date")
            .Add(x => x.Culture, German)
            .Add(x => x.Min, new DateOnly(2026, 1, 1))
            .Add(x => x.ValueChanged, _ => raised = true));

        picker.Find("input").Change("31.12.2025");

        raised.ShouldBeFalse();
        picker.Find(".cms-field-message-error").TextContent.ShouldBe("Enter a date on or after 01.01.2026.");
    }

    [Fact]
    public void TheTimePickerReadsTheTimeInTheCultureAndKeepsSecondsWhenAsked()
    {
        TimeOnly? value = null;
        var picker = Render<CmsTimePicker>(p => p
            .Add(x => x.Label, "Publish time")
            .Add(x => x.Required, true)
            .Add(x => x.Culture, English)
            .Add(x => x.Seconds, true)
            .Add(x => x.Value, new TimeOnly(9, 30, 15))
            .Add(x => x.ValueChanged, v => value = v));

        var element = picker.Find("input");
        element.GetAttribute("value").ShouldBe(new TimeOnly(9, 30, 15).ToString(English.DateTimeFormat.LongTimePattern, English));
        element.GetAttribute("aria-required").ShouldBe("true");
        picker.Find("label").GetAttribute("for").ShouldBe(element.Id);

        element.Change(new TimeOnly(14, 5, 7).ToString(English.DateTimeFormat.LongTimePattern, English));

        value.ShouldBe(new TimeOnly(14, 5, 7));
    }

    [Fact]
    public void WithoutSecondsTheTimePickerShowsTheShortTime()
    {
        var picker = Render<CmsTimePicker>(p => p.Add(x => x.Label, "Time").Add(x => x.Culture, German).Add(x => x.Value, new TimeOnly(9, 30)));

        picker.Find("input").GetAttribute("value").ShouldBe("09:30");
    }

    [Theory]
    [InlineData("en-US", "1234.5", 1234.5)]
    [InlineData("de-DE", "1234,5", 1234.5)]
    [InlineData("fr-FR", "-0,25", -0.25)]
    public void TheNumberInputReadsTheDecimalSeparatorOfTheCulture(string culture, string typed, double expected)
    {
        decimal? value = null;
        var input = Render<CmsNumberInput>(p => p
            .Add(x => x.Label, "Price")
            .Add(x => x.Culture, CultureInfo.GetCultureInfo(culture))
            .Add(x => x.ValueChanged, v => value = v));

        input.Find("input").Change(typed);

        value.ShouldBe((decimal)expected);
    }

    [Fact]
    public void TheNumberInputShowsItsValueInTheCultureWithoutGroupSeparators()
    {
        var input = Render<CmsNumberInput>(p => p.Add(x => x.Label, "Price").Add(x => x.Culture, German).Add(x => x.Value, 1234567.5m));

        input.Find("input").GetAttribute("value").ShouldBe("1234567,5");
        input.Find("input").GetAttribute("type").ShouldBe("text");
    }

    [Theory]
    [InlineData("1.5", false)]
    [InlineData("abc", false)]
    [InlineData("2,5", true)]
    public void TheNumberInputRefusesWhatItCannotReadAndAGroupSeparator(string typed, bool wholeNumbers)
    {
        var raised = false;
        var input = Render<CmsNumberInput>(p => p
            .Add(x => x.Label, "Count")
            .Add(x => x.Culture, German)
            .Add(x => x.WholeNumbers, wholeNumbers)
            .Add(x => x.ValueChanged, _ => raised = true));

        input.Find("input").Change(typed);

        raised.ShouldBeFalse();
        input.Find("input").GetAttribute("aria-invalid").ShouldBe("true");
        input.Find(".cms-field-message-error").TextContent.ShouldBe(wholeNumbers ? "Enter a whole number such as 1234." : "Enter a number such as 1234,5.");
    }

    [Fact]
    public void ANegativeNumberWhereNoneIsTakenSaysSo()
    {
        var input = Render<CmsNumberInput>(p => p.Add(x => x.Label, "Count").Add(x => x.Culture, English).Add(x => x.AllowNegative, false));

        input.Find("input").Change("-3");

        input.Find(".cms-field-message-error").TextContent.ShouldBe("Enter a number of zero or more.");
    }

    [Fact]
    public void AValueSetFromOutsideTakesBackTheErrorOfTextThatDidNotRead()
    {
        var picker = Render<CmsDatePicker>(p => p.Add(x => x.Label, "Day").Add(x => x.Culture, German));
        picker.Find("input").Change("next week");
        picker.FindAll(".cms-field-message-error").ShouldNotBeEmpty();

        picker.Render(p => p.Add(x => x.Value, new DateOnly(2026, 1, 2)));

        picker.FindAll(".cms-field-message-error").ShouldBeEmpty();
        picker.Find("input").HasAttribute("aria-invalid").ShouldBeFalse();
    }

    [Fact]
    public void ANumberInputWithoutNegativesAsksForTheNumericKeyboard()
    {
        var whole = Render<CmsNumberInput>(p => p.Add(x => x.Label, "Count").Add(x => x.WholeNumbers, true).Add(x => x.AllowNegative, false));
        var any = Render<CmsNumberInput>(p => p.Add(x => x.Label, "Delta"));

        whole.Find("input").GetAttribute("inputmode").ShouldBe("numeric");
        any.Find("input").HasAttribute("inputmode").ShouldBeFalse();
    }

    [Fact]
    public void AnEmptyNumberIsNoValue()
    {
        decimal? value = 3;
        var input = Render<CmsNumberInput>(p => p.Add(x => x.Label, "Count").Add(x => x.Value, value).Add(x => x.ValueChanged, v => value = v));

        input.Find("input").Change("");

        value.ShouldBeNull();
    }

    [Fact]
    public void AReadOnlyInputStaysFocusableAndTakesNoChange()
    {
        var raised = false;
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.ReadOnly, true).Add(x => x.ValueChanged, _ => raised = true));

        var element = input.Find("input");
        element.HasAttribute("readonly").ShouldBeTrue();
        element.HasAttribute("disabled").ShouldBeFalse();
        element.Input("Changed");

        raised.ShouldBeFalse();
    }

    [Fact]
    public void EveryInputTakesReadOnly()
    {
        Render<CmsTextArea>(p => p.Add(x => x.Label, "Summary").Add(x => x.ReadOnly, true)).Find("textarea").HasAttribute("readonly").ShouldBeTrue();
        Render<CmsNumberInput>(p => p.Add(x => x.Label, "Count").Add(x => x.ReadOnly, true)).Find("input").HasAttribute("readonly").ShouldBeTrue();
        Render<CmsDatePicker>(p => p.Add(x => x.Label, "Day").Add(x => x.ReadOnly, true)).Find("input").HasAttribute("readonly").ShouldBeTrue();
        Render<CmsTimePicker>(p => p.Add(x => x.Label, "Time").Add(x => x.ReadOnly, true)).Find("input").HasAttribute("readonly").ShouldBeTrue();

        // A native select has no read-only state.
        Render<CmsSelect>(p => p.Add(x => x.Label, "Status").Add(x => x.ReadOnly, true).Add(x => x.Options, [new CmsOption("draft", "Draft")]))
            .Find("select").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void LeavingAnInputRaisesOnBlur()
    {
        var left = 0;
        Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.OnBlur, () => left++)).Find("input").Blur();
        Render<CmsTextArea>(p => p.Add(x => x.Label, "Summary").Add(x => x.OnBlur, () => left++)).Find("textarea").Blur();
        Render<CmsNumberInput>(p => p.Add(x => x.Label, "Count").Add(x => x.OnBlur, () => left++)).Find("input").Blur();
        Render<CmsDatePicker>(p => p.Add(x => x.Label, "Day").Add(x => x.OnBlur, () => left++)).Find("input").Blur();
        Render<CmsTimePicker>(p => p.Add(x => x.Label, "Time").Add(x => x.OnBlur, () => left++)).Find("input").Blur();
        Render<CmsSelect>(p => p.Add(x => x.Label, "Status").Add(x => x.Options, [new CmsOption("draft", "Draft")]).Add(x => x.OnBlur, () => left++)).Find("select").Blur();

        left.ShouldBe(6);
    }

    [Fact]
    public void AMaximumLengthShowsACounterThatDescribesTheInput()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.Help, "Shown in lists.").Add(x => x.MaxLength, 10).Add(x => x.Value, "Hello"));

        var element = input.Find("input");
        element.HasAttribute("maxlength").ShouldBeFalse();
        var ids = element.GetAttribute("aria-describedby")!.Split(' ');
        ids.Length.ShouldBe(2);
        input.Find($"#{ids[1]}").TextContent.ShouldBe("5 of 10 characters");

        input.Render(p => p.Add(x => x.Value, "Hello, world"));

        input.Find(".cms-field-counter").ClassList.ShouldContain("cms-field-counter-over");
        input.Find(".cms-field-counter").TextContent.ShouldBe("12 of 10 characters");
    }

    [Fact]
    public void TheTextAreaCountsToo()
    {
        var area = Render<CmsTextArea>(p => p.Add(x => x.Label, "Summary").Add(x => x.MaxLength, 300).Add(x => x.Value, "abc").Add(x => x.AutoGrow, true));

        area.Find(".cms-field-counter").TextContent.ShouldBe("3 of 300 characters");
        area.Find("textarea").ClassList.ShouldContain("cms-textarea-grow");
    }

    [Fact]
    public void AClearableSelectKeepsItsEmptyOptionOnceAValueIsChosen()
    {
        string? value = "draft";
        var select = Render<CmsSelect>(p => p
            .Add(x => x.Label, "Status")
            .Add(x => x.Clearable, true)
            .Add(x => x.Options, [new CmsOption("draft", "Draft")])
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, v => value = v));

        select.FindAll("option")[0].GetAttribute("value").ShouldBe(string.Empty);
        select.Find("select").Change("");

        value.ShouldBeNull();
    }

    [Fact]
    public void ASelectWithAValueAndNoPlaceholderIsNotClearedUnlessClearable()
    {
        var select = Render<CmsSelect>(p => p
            .Add(x => x.Label, "Status")
            .Add(x => x.Options, [new CmsOption("draft", "Draft")])
            .Add(x => x.Value, "draft"));

        select.FindAll("option").Select(option => option.GetAttribute("value")).ShouldBe(["draft"]);
    }

    [Fact]
    public void ADisabledInputIsDisabled()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.Disabled, true));

        input.Find("input").HasAttribute("disabled").ShouldBeTrue();
    }

    // FocusAsync moves focus to the element that takes input, which a form calls for the first field with an error.
    [Fact]
    public async Task FocusAsyncFocusesTheInputOfEveryInput()
    {
        await ShouldFocusAsync(Render<CmsTextInput>(p => p.Add(x => x.Label, "Title")), input => input.FocusAsync());
        await ShouldFocusAsync(Render<CmsTextArea>(p => p.Add(x => x.Label, "Summary")), input => input.FocusAsync());
        await ShouldFocusAsync(Render<CmsSelect>(p => p.Add(x => x.Label, "Status").Add(x => x.Options, [new CmsOption("draft", "Draft")])), input => input.FocusAsync());
        await ShouldFocusAsync(Render<CmsDatePicker>(p => p.Add(x => x.Label, "Day")), input => input.FocusAsync());
        await ShouldFocusAsync(Render<CmsTimePicker>(p => p.Add(x => x.Label, "Time")), input => input.FocusAsync());
        await ShouldFocusAsync(Render<CmsCheckbox>(p => p.Add(x => x.Label, "Featured")), input => input.FocusAsync());
        await ShouldFocusAsync(Render<CmsSwitch>(p => p.Add(x => x.Label, "Visible")), input => input.FocusAsync());
    }

    [Fact]
    public void LangAndDirAreOnTheInputOnly()
    {
        var input = Render<CmsTextInput>(p => p.Add(x => x.Label, "Title").Add(x => x.Help, "Shown in lists.").Add(x => x.Lang, "ar").Add(x => x.Dir, "rtl"));

        var element = input.Find("input");
        (element.GetAttribute("lang"), element.GetAttribute("dir")).ShouldBe(("ar", "rtl"));
        input.Find("label").Closest("[lang]").ShouldBeNull();
        input.Find(".cms-field-message").Closest("[dir]").ShouldBeNull();
    }

    private async Task ShouldFocusAsync<TComponent>(IRenderedComponent<TComponent> input, Func<TComponent, ValueTask> focus)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var before = Focuses();
        await input.InvokeAsync(() => focus(input.Instance).AsTask());
        Focuses().ShouldBe(before + 1, typeof(TComponent).Name);
    }

    private int Focuses() => JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
}
