using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

// Cases from tailwind-merge's documentation and test suite, written for the Tailwind v4 utilities, plus the shadcn classes the
// admin's components carry.
public sealed class TailwindMergeTests
{
    [Theory]
    [InlineData("px-2 py-1 bg-red hover:bg-dark-red p-3 bg-[#B91C1C]", "hover:bg-dark-red p-3 bg-[#B91C1C]")]
    [InlineData("p-3 px-5", "p-3 px-5")]
    [InlineData("px-5 p-3", "p-3")]
    [InlineData("inset-x-4 right-4", "inset-x-4 right-4")]
    [InlineData("right-4 inset-x-4", "inset-x-4")]
    [InlineData("inset-x-px -inset-1", "-inset-1")]
    [InlineData("bottom-auto inset-y-6", "inset-y-6")]
    [InlineData("inline block", "block")]
    [InlineData("hidden flex", "flex")]
    [InlineData("mt-2 -mt-1", "-mt-1")]
    [InlineData("w-5 size-4", "size-4")]
    [InlineData("size-4 w-5", "size-4 w-5")]
    [InlineData("basis-1/2 flex-1", "flex-1")]
    [InlineData("grow shrink-0 flex-none", "flex-none")]
    [InlineData("gap-x-2 gap-4", "gap-4")]
    [InlineData("overflow-x-auto overflow-hidden", "overflow-hidden")]
    public void Later_layout_and_spacing_classes_replace_the_earlier_ones_they_cover(string input, string expected)
    {
        Assert.Equal(expected, TailwindMerge.Merge(input));
    }

    [Theory]
    [InlineData("ps-3 pe-9 px-4", "px-4")]
    [InlineData("px-4 pe-9", "px-4 pe-9")]
    [InlineData("ms-auto mx-2", "mx-2")]
    [InlineData("start-0 inset-x-0", "inset-x-0")]
    [InlineData("rounded-s-md rounded-ss-lg", "rounded-s-md rounded-ss-lg")]
    [InlineData("rounded-ss-lg rounded-s-md", "rounded-s-md")]
    [InlineData("rounded-tl-md rounded-md", "rounded-md")]
    [InlineData("border-s-2 border-x-4", "border-x-4")]
    [InlineData("text-start text-end", "text-end")]
    public void Logical_utilities_conflict_with_the_ones_that_cover_them(string input, string expected)
    {
        Assert.Equal(expected, TailwindMerge.Merge(input));
    }

    [Theory]
    [InlineData("text-sm text-lg", "text-lg")]
    [InlineData("text-sm text-muted-foreground", "text-sm text-muted-foreground")]
    [InlineData("text-muted-foreground text-destructive/90", "text-destructive/90")]
    [InlineData("text-[14px] text-base", "text-base")]
    [InlineData("text-[#fff] text-red-500", "text-red-500")]
    [InlineData("text-[length:var(--size)] text-[color:var(--tone)]", "text-[length:var(--size)] text-[color:var(--tone)]")]
    [InlineData("text-left text-sm text-center", "text-sm text-center")]
    [InlineData("text-wrap text-balance", "text-balance")]
    [InlineData("truncate text-ellipsis", "text-ellipsis")]
    [InlineData("font-medium font-semibold", "font-semibold")]
    [InlineData("font-sans font-mono", "font-mono")]
    [InlineData("font-medium font-mono", "font-medium font-mono")]
    [InlineData("leading-6 text-lg/7", "text-lg/7")]
    [InlineData("text-red text-lg/7 text-lg/8", "text-red text-lg/8")]
    [InlineData("text-lg/7 leading-6", "text-lg/7 leading-6")]
    [InlineData("underline line-through", "line-through")]
    [InlineData("underline underline-offset-4", "underline underline-offset-4")]
    [InlineData("uppercase normal-case", "normal-case")]
    public void Text_utilities_are_told_apart_by_their_value(string input, string expected)
    {
        Assert.Equal(expected, TailwindMerge.Merge(input));
    }

    [Theory]
    [InlineData("bg-black bg-(--my-color) bg-[color:var(--mystery-var)]", "bg-[color:var(--mystery-var)]")]
    [InlineData("bg-transparent bg-input/30", "bg-input/30")]
    [InlineData("bg-cover bg-red-500", "bg-cover bg-red-500")]
    [InlineData("bg-[url(/img.png)] bg-none", "bg-none")]
    [InlineData("bg-linear-to-r bg-[url(/img.png)] bg-red-500", "bg-[url(/img.png)] bg-red-500")]
    [InlineData("bg-no-repeat bg-repeat-x", "bg-repeat-x")]
    [InlineData("from-red-500 from-10% from-blue-500 from-20%", "from-blue-500 from-20%")]
    [InlineData("via-red-500 via-[30%] to-blue-500 to-90%", "via-red-500 via-[30%] to-blue-500 to-90%")]
    [InlineData("border border-2", "border-2")]
    [InlineData("border-transparent border-2 border-red-500", "border-2 border-red-500")]
    [InlineData("border border-input", "border border-input")]
    [InlineData("border-dashed border-solid", "border-solid")]
    [InlineData("border-[3px] border-[#ccc]", "border-[3px] border-[#ccc]")]
    [InlineData("border-x-red-500 border-red-600", "border-red-600")]
    [InlineData("ring ring-2", "ring-2")]
    [InlineData("ring-[3px] ring-ring/50", "ring-[3px] ring-ring/50")]
    [InlineData("ring-ring/50 ring-destructive/20", "ring-destructive/20")]
    [InlineData("ring-offset-2 ring-offset-background", "ring-offset-2 ring-offset-background")]
    [InlineData("outline-none outline-hidden", "outline-hidden")]
    [InlineData("outline outline-2 outline-ring/50", "outline-2 outline-ring/50")]
    [InlineData("shadow-sm shadow-lg", "shadow-lg")]
    [InlineData("shadow-xs shadow-black/5", "shadow-xs shadow-black/5")]
    [InlineData("shadow-[0_35px_60px_-15px_rgba(0,0,0,0.3)] shadow-none", "shadow-none")]
    [InlineData("rounded-md rounded-xs", "rounded-xs")]
    [InlineData("rounded rounded-[4px]", "rounded-[4px]")]
    public void Colour_width_and_style_utilities_with_one_prefix_are_kept_apart(string input, string expected)
    {
        Assert.Equal(expected, TailwindMerge.Merge(input));
    }

    [Theory]
    [InlineData("p-2 hover:p-4", "p-2 hover:p-4")]
    [InlineData("hover:p-2 hover:p-4", "hover:p-4")]
    [InlineData("hover:focus:p-2 focus:hover:p-4", "focus:hover:p-4")]
    [InlineData("dark:bg-input/30 bg-transparent", "dark:bg-input/30 bg-transparent")]
    [InlineData("data-[state=active]:bg-background data-[state=active]:bg-input/30", "data-[state=active]:bg-input/30")]
    [InlineData("[&>*]:underline [&>*]:line-through", "[&>*]:line-through")]
    [InlineData("[&>*]:underline [&_div]:line-through", "[&>*]:underline [&_div]:line-through")]
    [InlineData("supports-[display:grid]:grid supports-[display:grid]:flex", "supports-[display:grid]:flex")]
    [InlineData("[&_svg:not([class*='size-'])]:size-4 [&_svg:not([class*='size-'])]:size-3", "[&_svg:not([class*='size-'])]:size-3")]
    [InlineData("has-[>svg]:px-3 has-[>svg]:px-2.5", "has-[>svg]:px-2.5")]
    [InlineData("before:content-['a'] after:content-['b']", "before:content-['a'] after:content-['b']")]
    public void Classes_conflict_only_under_the_same_variants(string input, string expected)
    {
        Assert.Equal(expected, TailwindMerge.Merge(input));
    }

    [Theory]
    [InlineData("!p-3 !p-4 p-5", "!p-4 p-5")]
    [InlineData("p-3! p-4! p-5", "p-4! p-5")]
    [InlineData("!right-2 !-inset-x-1", "!-inset-x-1")]
    [InlineData("[padding:1rem] p-8", "[padding:1rem] p-8")]
    [InlineData("[padding:1rem] [padding:2rem]", "[padding:2rem]")]
    [InlineData("[mask-type:luminance] [mask-type:alpha]", "[mask-type:alpha]")]
    [InlineData("w-[calc(100%-2rem)] w-full", "w-full")]
    [InlineData("max-w-[calc(100%-2rem)] sm:max-w-lg max-w-sm", "sm:max-w-lg max-w-sm")]
    [InlineData("-translate-y-1/2 translate-y-0", "translate-y-0")]
    [InlineData("translate-x-2 translate-none", "translate-none")]
    [InlineData("grid-cols-[1fr,auto] grid-cols-2", "grid-cols-2")]
    [InlineData("transition-all transition-[color,box-shadow]", "transition-[color,box-shadow]")]
    [InlineData("transition-all transition-discrete", "transition-all transition-discrete")]
    public void Important_arbitrary_and_negative_classes_merge_like_the_others(string input, string expected)
    {
        Assert.Equal(expected, TailwindMerge.Merge(input));
    }

    [Theory]
    [InlineData("group peer @container/card-header my-custom-class", "group peer @container/card-header my-custom-class")]
    [InlineData("my-class my-class", "my-class my-class")]
    [InlineData("  p-2   \n p-3 ", "p-3")]
    [InlineData("", "")]
    public void Classes_it_does_not_know_are_kept_and_whitespace_is_normalised(string input, string expected)
    {
        Assert.Equal(expected, TailwindMerge.Merge(input));
    }

    [Fact]
    public void A_shadcn_button_with_a_class_parameter_keeps_only_the_callers_height_and_gap()
    {
        var classes = ButtonVariants.Classes("outline", "sm", "h-7 gap-1 w-full").Split(' ');

        Assert.Contains("h-7", classes);
        Assert.DoesNotContain("h-8", classes);
        Assert.Contains("gap-1", classes);
        Assert.DoesNotContain("gap-1.5", classes);
        Assert.DoesNotContain("gap-2", classes);
        Assert.Contains("w-full", classes);
        Assert.Contains("border", classes);
        Assert.Contains("bg-background", classes);
    }
}
