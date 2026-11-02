using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Schema;

public class ConditionTests
{
    private enum Category
    {
        News,
        PressRelease,
    }

    [Fact]
    public void EqualsReturnsACondition()
    {
        var condition = Condition.Field("category").Equals("video");

        condition.Operator.ShouldBe(ConditionOperator.Equal);
        condition.FieldName.ShouldBe("category");
        condition.Values.ShouldBe(["video"]);
        condition.Operands.ShouldBeEmpty();
    }

    [Fact]
    public void FieldNameIsConvertedToCamelCase()
    {
        Condition.Field("Category").IsEmpty().FieldName.ShouldBe("category");
        Condition.Field(nameof(Category.PressRelease)).IsEmpty().FieldName.ShouldBe("pressRelease");
        Condition.Field("URL").IsEmpty().FieldName.ShouldBe("url");
    }

    [Fact]
    public void FieldNeedsAName()
    {
        Should.Throw<ArgumentException>(() => Condition.Field(""));
    }

    [Fact]
    public void EnumMemberIsStoredAsItsCamelCaseName()
    {
        Condition.Field("category").Equals(Category.PressRelease).Values.ShouldBe(["pressRelease"]);
    }

    [Fact]
    public void ValuesAreReducedToWhatAFileCanHold()
    {
        var condition = Condition.Field("x").In(1, 2L, (byte)3, 4.0, 5.5m, 6.5f, true, "s", null, 'c', Guid.Empty);

        condition.Operator.ShouldBe(ConditionOperator.In);
        condition.Values.ShouldBe([1L, 2L, 3L, 4L, 5.5m, 6.5m, true, "s", null, "c", "00000000-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void WholeNumbersCompareEqualWhateverTheirType()
    {
        Condition.Field("x").Equals(5).Values.ShouldBe(Condition.Field("x").Equals(5.0m).Values);
        Condition.Field("x").Equals(5).Values.ShouldBe(Condition.Field("x").Equals(5.0).Values);
    }

    [Fact]
    public void NullIsAValue()
    {
        Condition.Field("x").Equals(null).Values.ShouldBe([null]);
    }

    [Fact]
    public void ValueOfAnotherTypeIsRejected()
    {
        Should.Throw<ArgumentException>(() => Condition.Field("x").Equals(new object()));
        Should.Throw<ArgumentException>(() => Condition.Field("x").Equals(new List<string>()));
        Should.Throw<ArgumentException>(() => Condition.Field("x").Equals(double.NaN));
    }

    [Fact]
    public void IsEmptyHasNoValues()
    {
        var condition = Condition.Field("videoUrl").IsEmpty();

        condition.Operator.ShouldBe(ConditionOperator.IsEmpty);
        condition.Values.ShouldBeEmpty();
    }

    [Fact]
    public void AndOrNotCombineConditions()
    {
        var a = Condition.Field("a").Equals(1);
        var b = Condition.Field("b").IsEmpty();

        var and = a.And(b);
        var or = a.Or(b);
        var not = a.Not();

        and.Operator.ShouldBe(ConditionOperator.And);
        and.Operands.ShouldBe([a, b]);
        or.Operator.ShouldBe(ConditionOperator.Or);
        or.Operands.ShouldBe([a, b]);
        not.Operator.ShouldBe(ConditionOperator.Not);
        not.Operands.ShouldBe([a]);
        and.FieldName.ShouldBeNull();
    }

    [Fact]
    public void CombiningTheSameOperatorTwiceFlattens()
    {
        var a = Condition.Field("a").Equals(1);
        var b = Condition.Field("b").Equals(2);
        var c = Condition.Field("c").Equals(3);

        a.And(b).And(c).Operands.ShouldBe([a, b, c]);
        a.And(b.And(c)).Operands.ShouldBe([a, b, c]);
        a.Or(b).And(c).Operands.Count.ShouldBe(2);
    }

    [Fact]
    public void ToStringReadsLikeTheCondition()
    {
        var condition = Condition.Field("category").Equals("video")
            .And(Condition.Field("tags").In("a", "b").Not())
            .Or(Condition.Field("videoUrl").IsEmpty());

        condition.ToString().ShouldBe("((category == \"video\") and (not (tags in [\"a\", \"b\"]))) or (videoUrl is empty)");
    }
}
