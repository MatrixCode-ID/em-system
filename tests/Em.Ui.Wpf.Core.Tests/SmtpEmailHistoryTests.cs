using Em.Ui.Wpf.Core;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Core.Tests;

public class SmtpEmailHistoryTests
{
   [Fact]
   public void RegistryHistoryIsRecentFirstBoundedDeduplicatedAndClearsOnlyItsOwnValues() {
      using var registry = new TestRegistry();
      var store = new SmtpEmailHistory(registry.Open);
      Assert.Empty(store.Read().From); Assert.Empty(store.Read().To);
      for (var i = 0; i < 25; i++) store.Remember($"sender{i}@example.com", $"recipient{i}@example.com");
      store.Remember(" SENDER10@example.com ", " recipient5@example.com ");
      var read = new SmtpEmailHistory(registry.Open).Read();
      Assert.Equal(SmtpEmailHistory.Limit, read.From.Length); Assert.Equal(SmtpEmailHistory.Limit, read.To.Length);
      Assert.Equal("SENDER10@example.com", read.From[0]); Assert.Equal("recipient5@example.com", read.To[0]);
      Assert.Single(read.From, email => email.Equals("sender10@example.com", StringComparison.OrdinalIgnoreCase));
      store.Remember("invalid\r\naddress", "Display <recipient@example.com>");
      Assert.Equal(read.From, store.Read().From); Assert.Equal(read.To, store.Read().To);
      using (var root = registry.Open()) {
         root.SetValue("OtherFeature", "keep");
         using var key = root.CreateSubKey("SmtpManager"); key.SetValue("OtherSetting", "keep");
         Assert.Equal(RegistryValueKind.MultiString, key.GetValueKind("FromHistory"));
      }
      store.Clear(); Assert.Empty(store.Read().From); Assert.Empty(store.Read().To);
      using (var root = registry.Open()) {
         Assert.Equal("keep", root.GetValue("OtherFeature"));
         using var key = root.OpenSubKey("SmtpManager"); Assert.Equal("keep", key!.GetValue("OtherSetting"));
      }
   }

   internal sealed class TestRegistry : IDisposable
   {
      private readonly string path = @"Software\Em.Tests\SmtpHistory\" + Guid.NewGuid().ToString("N");
      public RegistryKey Open() => Registry.CurrentUser.CreateSubKey(path);
      public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
   }
}
