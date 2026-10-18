using FluentCMS.Core;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.ChangeSets;

public sealed class ChangeOperationTests
{
    private static readonly Guid Item = Guid.NewGuid();

    [Theory]
    [InlineData(OperationKind.Create, ItemKind.Entry, null)]
    [InlineData(OperationKind.Update, ItemKind.Entity, 1)]
    [InlineData(OperationKind.Update, ItemKind.Media, 7)]
    [InlineData(OperationKind.Delete, ItemKind.Rule, 2)]
    [InlineData(OperationKind.Delete, ItemKind.Schema, 1)]
    public void An_operation_names_its_item_and_the_version_it_was_based_on(OperationKind kind, ItemKind itemKind, int? baseVersion)
    {
        var operation = ChangeOperation.Create(kind, itemKind, Item, baseVersion).Value;

        Assert.Equal(kind, operation.Kind);
        Assert.Equal(itemKind, operation.ItemKind);
        Assert.Equal(Item, operation.ItemId);
        Assert.Equal(baseVersion, operation.BaseVersion);
    }

    [Fact]
    public void An_operation_on_a_content_type_points_at_its_id_and_version()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        var fieldTypes = provider.GetRequiredService<FieldTypeRegistry>();
        var created = ContentType.Create(Guid.NewGuid(), "Product page", "product-page", new ContentTypeDefinition([]), fieldTypes).Value;
        var revised = created.Revise(new ContentTypeDefinition([new FieldDefinition("title", "text")]), fieldTypes).Value;

        var create = ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, created.Id, null).Value;
        var update = ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, revised.Id, revised.Version).Value;

        Assert.Equal(created.Id, create.ItemId);
        Assert.Null(create.BaseVersion);
        Assert.Equal(created.Id, update.ItemId);
        Assert.Equal(2, update.BaseVersion);
    }

    [Fact]
    public void An_operation_without_an_item_is_rejected()
    {
        var result = ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, Guid.Empty, 1);

        Assert.Equal(ChangeSetErrorCodes.ItemMissing, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void A_create_with_a_base_version_is_rejected()
    {
        var result = ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, Item, 1);

        Assert.Equal(ChangeSetErrorCodes.BaseVersionInvalid, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(OperationKind.Update, null)]
    [InlineData(OperationKind.Update, 0)]
    [InlineData(OperationKind.Delete, null)]
    [InlineData(OperationKind.Delete, -1)]
    public void An_update_or_delete_without_a_base_version_of_one_or_later_is_rejected(OperationKind kind, int? baseVersion)
    {
        var result = ChangeOperation.Create(kind, ItemKind.Entity, Item, baseVersion);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ChangeSetErrorCodes.BaseVersionInvalid, error.Code);
        Assert.Contains(kind.ToString().ToLowerInvariant(), error.Message);
    }

    [Fact]
    public void Every_problem_with_an_operation_is_reported_at_once()
    {
        var result = ChangeOperation.Create((OperationKind)9, (ItemKind)9, Guid.Empty, null);

        Assert.Equal(
            [ChangeSetErrorCodes.OperationKindInvalid, ChangeSetErrorCodes.ItemKindInvalid, ChangeSetErrorCodes.ItemMissing],
            result.Errors.Select(error => error.Code));
    }
}
