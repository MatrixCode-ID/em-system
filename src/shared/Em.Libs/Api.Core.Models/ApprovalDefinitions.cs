using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Em.Shared;
namespace Em.Api.Core.Models;
/// <summary>Declares a stable name readable by both startup validation and the build analyzer.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ApprovalAttribute(string name) : Attribute {
   /// <summary>Stable approval name, unique within its kind.</summary>
   public string Name { get; } = name;
}
/// <summary>Marker shared by all approval definitions.</summary>
public interface IEmApproval { }
/// <summary>Platform-neutral System approval definition.</summary>
public interface IEmApprovalSystem : IEmApproval {
   /// <summary>Display title; an empty value uses the declared approval name.</summary>
   string Title => string.Empty;
   /// <summary>Ordered levels and parallel steps.</summary>
   IReadOnlyList<ApprovalSystemStep> Steps { get; }
   /// <summary>Additional module claims allowed to void requests.</summary>
   IReadOnlyList<ClaimAction> VoidClaims => [];
   /// <summary>Whether server-side batch signing may bypass module client validation.</summary>
   bool AllowBatchSign => true;
}
/// <summary>System definition bound to the subject key understood by its module.</summary>
public interface IEmApprovalSystem<TKey> : IEmApprovalSystem where TKey : notnull { }
/// <summary>Reserved physical-document definition. Registration is not implemented.</summary>
public interface IEmApprovalDocument : IEmApproval { }
/// <summary>One server-defined step; all steps at the same level run in parallel.</summary>
public sealed record ApprovalSystemStep(int Level, string Name);
/// <summary>Stable subject serialization shared by API and clients.</summary>
public static class ApprovalSubjectKey {
   /// <summary>Preserves strings, and serializes objects in declared property order.</summary>
   public static string Serialize(object key) {
      ArgumentNullException.ThrowIfNull(key);
      var text = key as string ?? JsonSerializer.Serialize(key, key.GetType(), Defaults.ResponseJsonOptions);
      if (text.Length is 0 or > 400) throw new ArgumentException("The subject key must contain 1 to 400 characters.", nameof(key));
      return text;
   }
   /// <summary>Resolves the validated metadata name of a definition.</summary>
   public static string NameOf(Type definition) {
      var name = definition.GetCustomAttribute<ApprovalAttribute>()?.Name;
      if (!IsValidName(name)) throw new InvalidOperationException($"Approval definition '{definition.FullName}' needs a valid [Approval] name.");
      return name!;
   }
   /// <summary>Checks the ASCII name contract used by the database and analyzer.</summary>
   public static bool IsValidName(string? name) => name is { Length: > 0 and <= 50 } &&
      Regex.IsMatch(name, @"\A[A-Za-z][A-Za-z0-9]*(?: [A-Za-z0-9]+)*\z", RegexOptions.CultureInvariant);
}
