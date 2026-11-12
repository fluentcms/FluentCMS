using FluentCMS.Generators.Emit;
using Microsoft.CodeAnalysis;

namespace FluentCMS.Generators;

// FCMS0100 to FCMS0119 belong to the generator; a rule about the schema itself is an analyzer's (FCMS0001 to FCMS0099).
internal static class Descriptors
{
    private const string Category = "FluentCMS.Generators";

    public static readonly DiagnosticDescriptor TypeSkipped = new(
        EmitCodes.TypeSkipped,
        "A schema type gets no field builder",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownFieldType = new(
        EmitCodes.UnknownFieldType,
        "A field type has no field builder",
        "{0}",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberNameTaken = new(
        EmitCodes.MemberNameTaken,
        "The member name of a field is taken in its builder",
        "{0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingBuilder = new(
        EmitCodes.MissingBuilder,
        "A component or block type has no builder in this project",
        "{0}",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BlockNotReachable = new(
        EmitCodes.BlockNotReachable,
        "A filter path cannot name the block type",
        "{0}",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor For(string code) => code switch
    {
        EmitCodes.TypeSkipped => TypeSkipped,
        EmitCodes.UnknownFieldType => UnknownFieldType,
        EmitCodes.MemberNameTaken => MemberNameTaken,
        EmitCodes.BlockNotReachable => BlockNotReachable,
        _ => MissingBuilder,
    };
}
