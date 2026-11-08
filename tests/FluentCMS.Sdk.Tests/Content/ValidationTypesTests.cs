using System.Reflection;
using FluentCMS.Content;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Content;

public class ValidationTypesTests
{
    private static readonly ValidationError First = new("title", ValidationErrorCodes.Required, "A value is required.");
    private static readonly ValidationError Second = new("seo.metaTitle", ValidationErrorCodes.MaxLength, "Too long.");

    [Fact]
    public void SuccessIsValidAndHasNoErrors()
    {
        ValidationResult.Success.IsValid.ShouldBeTrue();
        ValidationResult.Success.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void AResultKeepsTheErrorsInOrder()
    {
        var result = new ValidationResult([Second, First]);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldBe([Second, First]);
    }

    [Fact]
    public void ErrorsAreComparedByValue()
    {
        new ValidationError("a", "FCMS2101", "m").ShouldBe(new ValidationError("a", "FCMS2101", "m"));
    }

    [Fact]
    public void TheExceptionCarriesTheErrorsAndNamesThem()
    {
        var exception = new ValidationException([First, Second]);

        exception.Errors.ShouldBe([First, Second]);
        exception.Message.ShouldBe("Validation failed with 2 errors: title: FCMS2101 A value is required.; seo.metaTitle: FCMS2103 Too long.");
        new ValidationException([First]).Message.ShouldBe("Validation failed: title: FCMS2101 A value is required.");
    }

    [Fact]
    public void TheExceptionNeedsAnError()
    {
        Should.Throw<ArgumentException>(() => new ValidationException([]));
    }

    [Fact]
    public void CodesAreDistinctAndInTheContentValidationGroup()
    {
        var codes = typeof(ValidationErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        codes.Count.ShouldBe(13);
        codes.Distinct().Count().ShouldBe(codes.Count);
        codes.ShouldAllBe(code => code.StartsWith("FCMS21", StringComparison.Ordinal) && code.Length == 8);
    }
}
