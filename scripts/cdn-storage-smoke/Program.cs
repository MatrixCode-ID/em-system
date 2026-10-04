// Run from repo root. All files are isolated below this script directory; no live CDN is touched.
using System.Reflection;
using System.Runtime.CompilerServices;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var fixtureParent = Path.GetFullPath("scripts/cdn-storage-smoke");
var fixture = Path.GetFullPath(Path.Combine(fixtureParent, "fixture-" + Guid.NewGuid().ToString("N")));
if (!fixture.StartsWith(fixtureParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException("Unexpected fixture path");
Directory.CreateDirectory(fixture);
var root = Path.Combine(fixture, "cdn");
Directory.CreateDirectory(root);
var storeType = typeof(CdnServices).Assembly.GetType("Em.Api.Core.CdnStore")!;
var store = storeType.GetMethod("Create")!.Invoke(null, [root, fixture, 1_000_000L])!;
var services = new ServiceCollection();
services.AddSingleton(storeType, store);
using var provider = services.BuildServiceProvider();
var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
typeof(EmApp).GetField("_rootServiceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, provider);
typeof(EmApp).GetField("_httpContextAccessor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, new HttpContextAccessor());
var service = new CdnServices();
typeof(ServicesBase).GetProperty(nameof(ServicesBase.App))!.SetValue(service, app);
var checks = 0;
void Check(string label, bool ok) {
   if (!ok) throw new InvalidOperationException("FAIL " + label);
   checks++; Console.WriteLine("PASS " + label);
}
void FileOf(string path, int size) {
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   File.WriteAllBytes(path, new byte[size]);
}
try {
   var empty = await service.GetMeta_CdnStorageSize();
   Check("empty CDN returns zero bytes/files", empty.TotalBytes == 0 && empty.FileCount == 0);
   FileOf(Path.Combine(root, "one.bin"), 100);
   FileOf(Path.Combine(root, "nested", "deep", "two.bin"), 200);
   FileOf(Path.Combine(root, "empty.bin"), 0);
   FileOf(Path.Combine(root, ".upload-test.tmp"), 1000);
   FileOf(Path.Combine(root, ".internal", "ignored.bin"), 1000);
   if (OperatingSystem.IsWindows()) {
      var hidden = Path.Combine(root, "hidden.bin");
      FileOf(hidden, 1000); File.SetAttributes(hidden, FileAttributes.Hidden);
      var system = Path.Combine(root, "system.bin");
      FileOf(system, 1000); File.SetAttributes(system, FileAttributes.System);
      var hiddenFolder = Path.Combine(root, "hidden-folder");
      FileOf(Path.Combine(hiddenFolder, "ignored.bin"), 1000);
      File.SetAttributes(hiddenFolder, FileAttributes.Hidden);
   }
   var actual = await service.GetMeta_CdnStorageSize();
   Check("nested files included; private/temp/hidden/system excluded", actual.TotalBytes == 300 && actual.FileCount == 3);
   var outside = Path.Combine(fixture, "outside");
   FileOf(Path.Combine(outside, "outside.bin"), 500);
   try {
      Directory.CreateSymbolicLink(Path.Combine(root, "outside-link"), outside);
      Directory.CreateSymbolicLink(Path.Combine(root, "cycle-link"), root);
      actual = await service.GetMeta_CdnStorageSize();
      Check("symlinks outside root and cycles excluded", actual.TotalBytes == 300 && actual.FileCount == 3);
   }
   catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException) {
      Console.WriteLine("SKIP symlink creation unavailable on this host");
   }
   File.Delete(Path.Combine(root, "one.bin"));
   actual = await service.GetMeta_CdnStorageSize();
   Check("refresh observes deleted files", actual.TotalBytes == 200 && actual.FileCount == 2);
   var attribute = typeof(CdnServices).GetMethod(nameof(CdnServices.GetMeta_CdnStorageSize))!.GetCustomAttribute<GetActionAttribute>();
   Check("GET requires CDN Manager Access", attribute?.Claim == ICdnServices.CdnClaim);
   typeof(ServicesBase).GetProperty(nameof(ServicesBase.AbortToken))!.SetValue(service, new CancellationToken(true));
   try { await service.GetMeta_CdnStorageSize(); throw new InvalidOperationException("Expected cancellation"); }
   catch (OperationCanceledException) { Check("scan supports cancellation", true); }
   services = new ServiceCollection();
   services.AddSingleton(storeType, storeType.GetProperty("Disabled")!.GetValue(null)!);
   using var disabledProvider = services.BuildServiceProvider();
   typeof(EmApp).GetField("_rootServiceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, disabledProvider);
   try { await service.GetMeta_CdnStorageSize(); throw new InvalidOperationException("Expected 404"); }
   catch (ActionException ex) { Check("disabled CDN returns 404", ex.StatusCode == 404); }
   Console.WriteLine($"{checks} checks passed.");
}
finally {
   if (!fixture.StartsWith(fixtureParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("Unsafe cleanup path");
   Directory.Delete(fixture, recursive: true);
}
