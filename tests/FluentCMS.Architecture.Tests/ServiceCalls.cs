using Mono.Cecil;
using Mono.Cecil.Cil;

namespace FluentCMS.Architecture.Tests;

// The members of the content and media services that an assembly calls, read from the IL: a dependency on the interface
// does not say whether a plugin reads through it or writes.
internal static class ServiceCalls
{
    private static readonly string[] Services = ["FluentCMS.Content.IContentService", "FluentCMS.Media.IMediaService"];

    public static List<string> In(string assembly)
    {
        using var module = ModuleDefinition.ReadModule(Path.Combine(AppContext.BaseDirectory, assembly + ".dll"));

        return [.. module.GetTypes()
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction => instruction.OpCode.FlowControl == FlowControl.Call)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Where(called => Services.Contains(called.DeclaringType.GetElementType().FullName.Split('`')[0]))
            .Select(called => $"{called.DeclaringType.GetElementType().FullName.Split('`')[0]}.{called.Name}")
            .Distinct()];
    }
}
