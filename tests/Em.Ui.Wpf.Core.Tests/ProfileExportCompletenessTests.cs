using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json.Serialization;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Core.Tests
{
   /// <summary>
   /// Guards that Export/Import keeps every setting of a Container Manager, NuGet Manager and Release Manager profile.
   /// A new profile property that Export or Import drops makes these tests fail; fix Export/Import, not the test.
   /// </summary>
   public sealed class ProfileExportCompletenessTests : IDisposable
   {
      private readonly string folder = Path.Combine(Path.GetTempPath(), "em-export-complete-" + Guid.NewGuid().ToString("N"));

      public void Dispose() {
         try {
            Directory.Delete(folder, true);
         } catch (IOException) {
         } catch (UnauthorizedAccessException) {
         }
      }

      // Every setting is set to a non-default value (last enum value, true, 7, text naming its path).
      private static object Fill(Type type, string path, int depth, HashSet<string> skip) {
         if (type == typeof(string)) return "v:" + path;
         if (type == typeof(bool)) return true;
         if (type == typeof(int)) return 7;
         if (type.IsEnum) { var values = Enum.GetValues(type); return values.GetValue(values.Length - 1)!; }
         if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
            var list = (IList)Activator.CreateInstance(type)!;
            if (depth < 6) list.Add(Fill(type.GetGenericArguments()[0], path + "[0]", depth + 1, skip));
            return list;
         }
         var instance = Activator.CreateInstance(type)!;
         foreach (var property in type.GetProperties()) {
            if (!property.CanWrite || property.GetCustomAttribute<JsonIgnoreAttribute>() != null || skip.Contains(path + "." + property.Name)) continue;
            property.SetValue(instance, Fill(property.PropertyType, path + "." + property.Name, depth + 1, skip));
         }
         return instance;
      }

      private static void Compare(object? expected, object? actual, string path, List<string> differences) {
         if (expected == null || actual == null) { if (expected != actual) differences.Add(path); return; }
         var type = expected.GetType();
         if (type.IsPrimitive || type.IsEnum || type == typeof(string)) { if (!Equals(expected, actual)) differences.Add($"{path}: {expected} => {actual}"); return; }
         if (expected is IList left) {
            var right = (IList)actual;
            if (left.Count != right.Count) differences.Add(path + " (count)");
            for (var i = 0; i < Math.Min(left.Count, right.Count); i++) Compare(left[i], right[i], $"{path}[{i}]", differences);
            return;
         }
         foreach (var property in type.GetProperties()) {
            if (property.GetIndexParameters().Length > 0 || property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
            Compare(property.GetValue(expected), property.GetValue(actual), path + "." + property.Name, differences);
         }
      }

      [Theory]
      [InlineData(PublishKind.Container)]
      [InlineData(PublishKind.NuGet)]
      public void PublishProfile_ExportThenImport_KeepsEverySetting(PublishKind kind) {
         var profile = PublishProfile.Create(kind);
         var skip = new HashSet<string> { "PublishProfile.Id", "PublishProfile.Kind", "PublishProfile.FormatVersion", "PublishProfile.Credentials", "PublishProfile.SensitiveDataStorage" };
         foreach (var property in typeof(PublishProfile).GetProperties()) {
            if (!property.CanWrite || property.GetCustomAttribute<JsonIgnoreAttribute>() != null || skip.Contains("PublishProfile." + property.Name)) continue;
            if (property.Name == "Container" && kind != PublishKind.Container || property.Name == "NuGet" && kind != PublishKind.NuGet) continue;
            property.SetValue(profile, Fill(property.PropertyType, "PublishProfile." + property.Name, 0, skip));
         }
         profile.Workspace = Path.Combine(folder, "ws");
         Directory.CreateDirectory(profile.Workspace);
         profile.ReleaseNotesFolder = Path.Combine(folder, "notes");
         var file = Path.Combine(folder, "export.json");

         new ProfileStore(Path.Combine(folder, "source")).Export(profile, file, ExportSecrets.None);
         var imported = new ProfileStore(Path.Combine(folder, "target")).Import(file).Profile!;

         var expected = profile.Clone();
         expected.Id = imported.Id;
         expected.Workspace = imported.Workspace;
         var differences = new List<string>();
         Compare(expected, imported, "profile", differences);
         Assert.True(differences.Count == 0, "Export/Import dropped: " + string.Join("; ", differences));
         Assert.True(imported.ReleaseNoteLock);
         Assert.Equal(profile.ReleaseNotesFolder, imported.ReleaseNotesFolder);
      }

      [Fact]
      public void ReleaseProfile_ExportThenImport_KeepsEverySetting() {
         var skip = new HashSet<string> { "ReleaseProfile.Id", "ReleaseProfile.FormatVersion", "ReleaseProfile.PrivateKeyPem" };
         var profile = (ReleaseProfile)Fill(typeof(ReleaseProfile), "ReleaseProfile", 0, skip);
         profile.Name = "Contoso Inventory";
         profile.PrivateKeyPem = ReleasePemKey.Create();
         var source = new ReleaseProfileStore(Path.Combine(folder, "release-source"));
         source.Save(profile);
         var file = Path.Combine(folder, "release-export.json");

         source.Export(profile.Id, file);
         var imported = new ReleaseProfileStore(Path.Combine(folder, "release-target")).Import(file, false);

         var differences = new List<string>();
         Compare(profile, imported, "profile", differences);
         Assert.True(differences.Count == 0, "Export/Import dropped: " + string.Join("; ", differences));
         Assert.True(imported.ReleaseNoteLock);
      }
   }
}
