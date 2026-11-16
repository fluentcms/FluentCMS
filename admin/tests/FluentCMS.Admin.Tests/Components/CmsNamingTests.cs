using Bunit;
using FluentCMS.Admin.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

// A control without an accessible name is refused when it renders, also when it is built in code without [EditorRequired].
public sealed class CmsNamingTests : ComponentTestContext
{
    [Fact]
    public void AnInputWithoutALabelIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsTextInput>()).Message.ShouldContain("Label");

    [Fact]
    public void ASelectWithABlankLabelIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsSelect>(p => p.Add(x => x.Label, " ")));

    [Fact]
    public void ACheckboxWithoutALabelIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsCheckbox>());

    [Fact]
    public void AGridWithoutALabelIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsDataGrid<string>>(p => p.Add(x => x.Items, ["a"])));

    [Fact]
    public void ATreeWithoutALabelIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsTree>());

    [Fact]
    public void AMenuWithoutALabelIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsMenu>());

    [Fact]
    public void ADropZoneWithoutALabelIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsFileDropZone>());

    [Fact]
    public void ADialogWithoutATitleIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsDialog>()).Message.ShouldContain("Title");

    [Fact]
    public void ADrawerWithoutATitleIsRefused() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsDrawer>());
}
