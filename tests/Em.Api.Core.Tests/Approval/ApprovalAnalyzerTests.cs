using System.Collections.Immutable;
using Em.Api.Core.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Em.Api.Core.Tests.Approval;

public sealed class ApprovalAnalyzerTests {
   private const string Contracts = """
      namespace Em.Api.Core.Models {
         public interface IEmApproval { }
         public interface IEmApprovalSystem<T> : IEmApproval { }
         [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class ApprovalAttribute(string name) : System.Attribute { }
      }
      namespace Em.Api.Core.Approval {
         public interface IEmApprovalData<T> : Em.Api.Core.Models.IEmApproval { }
         public interface IEmApprovalDataChild<T,P> : Em.Api.Core.Models.IEmApproval { }
      }
      """;
   private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
      .Distinct().Select(path => MetadataReference.CreateFromFile(path)).ToArray();
   private static CSharpCompilation Compilation(string source, IEnumerable<MetadataReference>? extra = null) => CSharpCompilation.Create(Guid.NewGuid().ToString("N"),
      [CSharpSyntaxTree.ParseText(source)], References.Concat(extra ?? []), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

   [Theory]
   [InlineData("public class A : Em.Api.Core.Models.IEmApprovalSystem<string> {}", "EMAPV001")]
   [InlineData("[Em.Api.Core.Models.Approval(\"Bad-Name\")] public class A : Em.Api.Core.Models.IEmApprovalSystem<string> {}", "EMAPV002")]
   [InlineData("[Em.Api.Core.Models.Approval(\"Same\")] public class A : Em.Api.Core.Models.IEmApprovalSystem<string> {} [Em.Api.Core.Models.Approval(\"same\")] public class B : Em.Api.Core.Models.IEmApprovalSystem<string> {}", "EMAPV003")]
   [InlineData("[Em.Api.Core.Models.Approval(\"One\")] public class A : Em.Api.Core.Approval.IEmApprovalData<string> {} [Em.Api.Core.Models.Approval(\"Two\")] public class B : Em.Api.Core.Approval.IEmApprovalData<string> {}", "EMAPV004")]
   [InlineData("[Em.Api.Core.Models.Approval(\"Child\")] public class A : Em.Api.Core.Approval.IEmApprovalDataChild<string,int> {}", "EMAPV005")]
   public async Task ReportsEachMetadataError(string source, string expected) {
      var compilation = Compilation(Contracts + source);
      Assert.DoesNotContain(compilation.GetDiagnostics(), x => x.Severity == DiagnosticSeverity.Error);
      var result = await compilation.WithAnalyzers([new ApprovalDefinitionAnalyzer()]).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
      Assert.Contains(result, x => x.Id == expected);
      Assert.DoesNotContain(result, x => x.Id == "AD0001");
   }

   [Fact]
   public async Task DetectsDuplicatesAcrossReferencedModules() {
      var first = Compilation(Contracts + "[Em.Api.Core.Models.Approval(\"Shared\")] public class A : Em.Api.Core.Models.IEmApprovalSystem<string> {}");
      using var binary = new MemoryStream();
      Assert.True(first.Emit(binary).Success);
      var second = Compilation("[Em.Api.Core.Models.Approval(\"Shared\")] public class B : Em.Api.Core.Models.IEmApprovalSystem<string> {}",
         [MetadataReference.CreateFromImage(binary.ToArray())]);
      var result = await second.WithAnalyzers([new ApprovalDefinitionAnalyzer()]).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
      Assert.Contains(result, x => x.Id == "EMAPV003");
   }
}
