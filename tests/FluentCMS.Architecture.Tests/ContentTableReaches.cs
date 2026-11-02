using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace FluentCMS.Architecture.Tests;

// The calls that reach a content table without naming a class, read from the IL: a property bag by table name, and SQL.
internal static class ContentTableReaches
{
    private static readonly string[] Allowed = ["FluentCMS.Core.Content", "FluentCMS.Core.Storage", "FluentCMS.Data", "FluentCMS.Cli"];

    public static IEnumerable<string> SourceAssemblies() =>
        ProjectFile.SourceProjects.Select(project => Path.Combine(AppContext.BaseDirectory, project.Name + ".dll")).Where(File.Exists);

    public static IEnumerable<string> InEveryAssembly() => SourceAssemblies().SelectMany(In);

    public static List<string> In(string assembly)
    {
        using var module = ModuleDefinition.ReadModule(assembly);

        List<string> violations = [];
        foreach (var type in module.GetTypes().Where(type => !MayReach(type)))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                violations.AddRange(method.Body.Instructions
                    .Where(instruction => instruction.OpCode.FlowControl == FlowControl.Call && instruction.Operand is MethodReference called && IsReach(called))
                    .Select(instruction => $"{type.FullName}.{method.Name} calls {((MethodReference)instruction.Operand).FullName}"));
            }
        }

        return violations;
    }

    private static bool MayReach(TypeDefinition type)
    {
        var root = type;
        while (root.DeclaringType is not null)
        {
            root = root.DeclaringType;
        }

        return Allowed.Any(name => root.Namespace == name || root.Namespace.StartsWith(name + ".", StringComparison.Ordinal));
    }

    private static bool IsReach(MethodReference called)
    {
        var declaring = called.DeclaringType.FullName;
        return called switch
        {
            GenericInstanceMethod { ElementMethod.Name: "Set" } set => set.Parameters.Count > 0 || set.GenericArguments[0].FullName.StartsWith("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal),
            { Name: "get_Database" } => true,
            { Name: "Rows" } => declaring == typeof(CmsDbContext).FullName,
            _ => declaring is "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions" or "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade"
                || (declaring == "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions" && called.Name.StartsWith("FromSql", StringComparison.Ordinal)),
        };
    }
}

// What the scan must catch, in a namespace it does not allow. It is never run.
internal static class CarelessCode
{
    public static object ByName(CmsDbContext context) => context.Set<Dictionary<string, object>>("cms_article");

    public static object ByRows(CmsDbContext context) => context.Rows("cms_article");

    public static int BySql(CmsDbContext context) => context.Database.ExecuteSqlRaw("UPDATE cms_article SET views = 0");

    public static object ByQuery(CmsDbContext context) => context.Documents.FromSqlRaw("SELECT * FROM cms_article");
}
