using Bunit;
using FluentCMS.Admin.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed class CmsInputTests : ComponentTestContext
{
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
    public void TheDatePickerIsANativeDateInputThatIsLabelledDescribedAndParsesTheValue()
    {
        DateOnly? value = null;
        var picker = Render<CmsDatePicker>(p => p
            .Add(x => x.Label, "Publish date")
            .Add(x => x.Error, "Pick a date.")
            .Add(x => x.ValueChanged, v => value = v));

        var element = picker.Find("input");
        element.GetAttribute("type").ShouldBe("date");
        picker.Find("label").GetAttribute("for").ShouldBe(element.Id);
        element.GetAttribute("aria-invalid").ShouldBe("true");
        picker.Find($"#{element.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Pick a date.");

        element.Change("2026-11-15");

        value.ShouldBe(new DateOnly(2026, 11, 15));
    }

    [Fact]
    public void TheDatePickerShowsItsValueInTheFormatOfTheInput()
    {
        var picker = Render<CmsDatePicker>(p => p.Add(x => x.Label, "Publish date").Add(x => x.Value, new DateOnly(2026, 1, 2)));

        picker.Find("input").GetAttribute("value").ShouldBe("2026-01-02");
    }

    [Fact]
    public void TheTimePickerIsANativeTimeInputThatParsesTheValue()
    {
        TimeOnly? value = null;
        var picker = Render<CmsTimePicker>(p => p
            .Add(x => x.Label, "Publish time")
            .Add(x => x.Required, true)
            .Add(x => x.ValueChanged, v => value = v));

        var element = picker.Find("input");
        element.GetAttribute("type").ShouldBe("time");
        element.GetAttribute("aria-required").ShouldBe("true");
        picker.Find("label").GetAttribute("for").ShouldBe(element.Id);

        element.Change("09:30");

        value.ShouldBe(new TimeOnly(9, 30));
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
