using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Em.Api.Core.Analyzers;

/// <summary>Validates approval metadata in the compilation and referenced module assemblies.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApprovalDefinitionAnalyzer : DiagnosticAnalyzer {
   private static DiagnosticDescriptor Rule(string id, string title, string message) =>
      new DiagnosticDescriptor(id, title, message, "Approval", DiagnosticSeverity.Error, true);
   private static readonly DiagnosticDescriptor Missing = Rule("EMAPV001", "Missing approval name", "Approval definition '{0}' requires [Approval]");
   private static readonly DiagnosticDescriptor Invalid = Rule("EMAPV002", "Invalid approval name", "Approval name '{0}' must contain at most 50 ASCII letters, digits and single spaces, starting with a letter");
   private static readonly DiagnosticDescriptor Duplicate = Rule("EMAPV003", "Duplicate approval name", "Duplicate {0} approval '{1}': {2}");
   private static readonly DiagnosticDescriptor Entity = Rule("EMAPV004", "Duplicate Data entity", "Data entity '{0}' is owned by multiple definitions: {1}");
   private static readonly DiagnosticDescriptor Child = Rule("EMAPV005", "Named child definition", "Child definition '{0}' must not declare [Approval]");
   /// <inheritdoc />
   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Missing, Invalid, Duplicate, Entity, Child);
   /// <inheritdoc />
   public override void Initialize(AnalysisContext context) {
      context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
      context.EnableConcurrentExecution();
      context.RegisterCompilationAction(Analyze);
   }
   private static void Analyze(CompilationAnalysisContext context) {
      var named = new List<(string Kind, string Name, INamedTypeSymbol Type)>();
      var entities = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
      var assemblies = context.Compilation.SourceModule.ReferencedAssemblySymbols.Concat(new[] { context.Compilation.Assembly });
      foreach (var assembly in assemblies) {
         foreach (var type in Types(assembly.GlobalNamespace)) {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (type.IsAbstract || type.TypeKind != TypeKind.Class || type.IsGenericType) continue;
            var interfaces = type.AllInterfaces;
            if (!interfaces.Any(i => i.ToDisplayString() == "Em.Api.Core.Models.IEmApproval")) continue;
            var data = interfaces.FirstOrDefault(i => i.MetadataName == "IEmApprovalData`1" && i.ContainingNamespace.ToDisplayString() == "Em.Api.Core.Approval");
            var child = interfaces.FirstOrDefault(i => i.MetadataName == "IEmApprovalDataChild`2" && i.ContainingNamespace.ToDisplayString() == "Em.Api.Core.Approval");
            var system = interfaces.Any(i => i.MetadataName == "IEmApprovalSystem`1" && i.ContainingNamespace.ToDisplayString() == "Em.Api.Core.Models");
            var document = interfaces.Any(i => i.ToDisplayString() == "Em.Api.Core.Models.IEmApprovalDocument");
            var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Em.Api.Core.Models.ApprovalAttribute");
            if (child != null && attribute != null) Report(Child, type, type.Name);
            if (data != null || child != null) {
               var entityName = (data ?? child)!.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
               if (!entities.TryGetValue(entityName, out var owners)) entities.Add(entityName, owners = new List<INamedTypeSymbol>());
               owners.Add(type);
            }
            if (child != null || (data == null && !system && !document)) continue;
            if (attribute == null) { Report(Missing, type, type.Name); continue; }
            var name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            if (name == null || name.Length > 50 || !Regex.IsMatch(name, @"\A[A-Za-z][A-Za-z0-9]*(?: [A-Za-z0-9]+)*\z"))
               Report(Invalid, type, name ?? "<null>");
            else named.Add((data != null ? "Data" : system ? "System" : "Document", name, type));
         }
      }
      foreach (var group in named.GroupBy(x => x.Kind + ":" + x.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) {
         var first = group.First();
         Report(Duplicate, first.Type, first.Kind, first.Name, string.Join(", ", group.Select(x => x.Type.ToDisplayString())));
      }
      foreach (var group in entities.Where(g => g.Value.Count > 1))
         Report(Entity, group.Value[0], group.Key, string.Join(", ", group.Value.Select(t => t.ToDisplayString())));
      void Report(DiagnosticDescriptor rule, INamedTypeSymbol type, params object[] args) =>
         context.ReportDiagnostic(Diagnostic.Create(rule, type.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None, args));
   }
   private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns) {
      foreach (var child in ns.GetNamespaceMembers()) foreach (var type in Types(child)) yield return type;
      foreach (var type in ns.GetTypeMembers()) foreach (var nested in Nested(type)) yield return nested;
   }
   private static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type) {
      yield return type;
      foreach (var child in type.GetTypeMembers()) foreach (var nested in Nested(child)) yield return nested;
   }
}
