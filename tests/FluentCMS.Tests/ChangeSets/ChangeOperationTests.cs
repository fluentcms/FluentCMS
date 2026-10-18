using FluentCMS.Core;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.ChangeSets;

public sealed class ChangeOperationTests
{
    private static readonly Guid Item = Guid.NewGuid();

    [Theory]
    [InlineData(OperationKind.Create, ItemKind.Entry, null, 1)]
    [InlineData(OperationKind.Update, ItemKind.Entity, 1, 2)]
    [InlineData(OperationKind.Update, ItemKind.Media, 7, 9)]
    [InlineData(OperationKind.Delete, ItemKind.Rule, 2, null)]
    [InlineData(OperationKind.Delete, ItemKind.Schema, 1, null)]
    public void An_operation_names_its_item_and_the_versions_it_starts_from_and_proposes(
        OperationKind kind,
        ItemKind itemKind,
        int? baseVersion,
        int? proposedVersion)
    {
        var operation = ChangeOperation.Create(kind, itemKind, Item, baseVersion, proposedVersion).Value;

        Assert.Equal(kind, operation.Kind);
        Assert.Equal(itemKind, operation.ItemKind);
        Assert.Equal(Item, operation.ItemId);
        Assert.Equal(baseVersion, operation.BaseVersion);
        Assert.Equal(proposedVersion, operation.ProposedVersion);
    }

    [Fact]
    public void An_operation_on_a_content_type_points_at_its_id_and_versions()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        var fieldTypes = provider.GetRequiredService<FieldTypeRegistry>();
        var created = ContentType.Create(Guid.NewGuid(), "Product page", "product-page", new ContentTypeDefinition([]), fieldTypes).Value;
        var revised = created.Revise(new ContentTypeDefinition([new FieldDefinition("title", "text")]), fieldTypes).Value;

        var create = ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, created.Id, null, created.Version).Value;
        var update = ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, revised.Id, created.Version, revised.Version).Value;

        Assert.Equal(created.Id, create.ItemId);
        Assert.Null(create.BaseVersion);
        Assert.Equal(1, create.ProposedVersion);
        Assert.Equal(created.Id, update.ItemId);
        Assert.Equal(1, update.BaseVersion);
        Assert.Equal(2, update.ProposedVersion);
    }

    [Fact]
    public void An_operation_without_an_item_is_rejected()
    {
        var result = ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, Guid.Empty, 1, 2);

        Assert.Equal(ChangeSetErrorCodes.ItemMissing, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void A_create_with_a_base_version_is_rejected()
    {
        var result = ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, Item, 1, 2);

        Assert.Equal(ChangeSetErrorCodes.BaseVersionInvalid, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(OperationKind.Update, null)]
    [InlineData(OperationKind.Update, 0)]
    [InlineData(OperationKind.Delete, null)]
    [InlineData(OperationKind.Delete, -1)]
    public void An_update_or_delete_without_a_base_version_of_one_or_later_is_rejected(OperationKind kind, int? baseVersion)
    {
        var result = ChangeOperation.Create(kind, ItemKind.Entity, Item, baseVersion, kind == OperationKind.Update ? 2 : null);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ChangeSetErrorCodes.BaseVersionInvalid, error.Code);
        Assert.Contains(kind.ToString().ToLowerInvariant(), error.Message);
    }

    [Theory]
    [InlineData(OperationKind.Create, null, null)]
    [InlineData(OperationKind.Create, null, 0)]
    [InlineData(OperationKind.Update, 3, null)]
    [InlineData(OperationKind.Update, 3, 3)]
    [InlineData(OperationKind.Update, 3, 2)]
    [InlineData(OperationKind.Delete, 3, 4)]
    public void A_create_or_update_proposes_a_later_version_and_a_delete_proposes_none(OperationKind kind, int? baseVersion, int? proposedVersion)
    {
        var result = ChangeOperation.Create(kind, ItemKind.Entry, Item, baseVersion, proposedVersion);

        Assert.Equal(ChangeSetErrorCodes.ProposedVersionInvalid, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Every_problem_with_an_operation_is_reported_at_once()
    {
        var result = ChangeOperation.Create((OperationKind)9, (ItemKind)9, Guid.Empty, null, null);

        Assert.Equal(
            [ChangeSetErrorCodes.OperationKindInvalid, ChangeSetErrorCodes.ItemKindInvalid, ChangeSetErrorCodes.ItemMissing],
            result.Errors.Select(error => error.Code));
    }

    [Theory]
    [InlineData(OperationKind.Create, null, 1, OperationKind.Delete, 1, null)]
    [InlineData(OperationKind.Update, 2, 5, OperationKind.Update, 5, 2)]
    [InlineData(OperationKind.Delete, 3, null, OperationKind.Create, null, 3)]
    public void The_inverse_puts_the_item_back_at_the_version_the_operation_was_based_on(
        OperationKind kind,
        int? baseVersion,
        int? proposedVersion,
        OperationKind inverseKind,
        int? inverseBase,
        int? inverseProposed)
    {
        var operation = ChangeOperation.Create(kind, ItemKind.Schema, Item, baseVersion, proposedVersion).Value;

        var inverse = operation.Inverse();

        Assert.Equal(inverseKind, inverse.Kind);
        Assert.Equal(ItemKind.Schema, inverse.ItemKind);
        Assert.Equal(Item, inverse.ItemId);
        Assert.Equal(inverseBase, inverse.BaseVersion);
        Assert.Equal(inverseProposed, inverse.ProposedVersion);
        Assert.Equal(operation, inverse.Inverse());
    }

    [Fact]
    public void An_update_back_to_an_earlier_version_exists_only_as_an_inverse()
    {
        var created = ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, Item, 5, 2);

        Assert.Equal(ChangeSetErrorCodes.ProposedVersionInvalid, Assert.Single(created.Errors).Code);
    }

    [Fact]
    public void A_stored_operation_is_restored_without_being_checked_again()
    {
        var restored = ChangeOperation.Restore(OperationKind.Update, ItemKind.Entry, Item, 3, 3);

        Assert.Equal(3, restored.BaseVersion);
        Assert.Equal(3, restored.ProposedVersion);
    }
}
