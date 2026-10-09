using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using System.Runtime.CompilerServices;

namespace Em.Ui.Wpf.Core.Tests;

public class SmtpManagerTests
{
   // Loads XAML resources only: no window, layout render, screenshot or live application.
   [WpfFact]
   public async Task ManagerXamlLoadsSharedStylesWithoutRendering() {
      var app = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
      var view = new SmtpManager(app);
      Assert.IsType<System.Windows.Style>(view.Resources["fieldPasswordStyle"]);
      Assert.IsType<System.Windows.Controls.PasswordBox>(view.FindName("passwordBox"));
      foreach (var name in new[] { "testSender", "testRecipient" }) {
         var combo = Assert.IsType<System.Windows.Controls.ComboBox>(view.FindName(name));
         Assert.True(combo.IsEditable); combo.ApplyTemplate();
         Assert.IsType<System.Windows.Controls.TextBox>(combo.Template.FindName("PART_EditableTextBox", combo));
      }
      var smtp = Assert.IsType<System.Windows.Controls.ComboBox>(view.FindName("testSmtp"));
      var profile = new SmtpProfileDetail { Id = "profile-id", Name = "Primary relay" };
      view.Vm.Profiles.Add(profile); smtp.SelectedItem = profile; smtp.ApplyTemplate();
      Assert.Same(profile, view.Vm.TestProfile);
      Assert.Same(smtp.ItemTemplate, smtp.SelectionBoxItemTemplate);
      var caption = Assert.IsType<System.Windows.Controls.TextBlock>(smtp.ItemTemplate.LoadContent());
      caption.DataContext = profile;
      await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.DataBind);
      caption.GetBindingExpression(System.Windows.Controls.TextBlock.TextProperty)!.UpdateTarget();
      Assert.Equal("Primary relay", caption.Text);
      Assert.False(view.Vm.CanEdit);
   }

   [Fact]
   public async Task LoadSaveAndReloadKeepSecretOutOfLoadedStateAndClearDraft() {
      var service = new StubService(); var vm = new SmtpManagerVm(service);
      Assert.False(vm.CanEdit); Assert.False(vm.Commands[nameof(vm.SaveCommand)]!.CanExecute());
      await vm.ReloadAsync();
      Assert.True(vm.CanEdit); Assert.Equal(7, vm.Revision); Assert.True(vm.HasPassword);
      var changes = new List<string?>(); vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
      vm.Authenticate = false;
      Assert.False(vm.Settings.Authenticate); Assert.Contains(nameof(vm.Authenticate), changes);
      vm.NewPassword = "new-secret";
      await vm.SaveCommand();
      Assert.Equal(7, service.LastSave!.ExpectedRevision); Assert.Equal("new-secret", service.LastSave.Password);
      Assert.Null(vm.NewPassword); Assert.Equal(8, vm.Revision);
      await vm.SaveCommand();
      Assert.Null(service.LastSave!.Password);
      vm.NewPassword = "draft"; vm.ClearPassword = true;
      await vm.ReloadAsync();
      Assert.Null(vm.NewPassword); Assert.False(vm.ClearPassword);
   }

   [Fact]
   public async Task BusyPreventsDuplicateCallsAndRecoversAfterError() {
      var service = new StubService(); var vm = new SmtpManagerVm(service);
      await vm.ReloadAsync();
      service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
      var loading = vm.ReloadAsync();
      Assert.False(vm.CanEdit); Assert.False(vm.Commands[nameof(vm.RefreshCommand)]!.CanExecute());
      await vm.ReloadAsync(); Assert.Equal(2, service.Reads);
      service.Pending.SetException(new ActionException("Reload needed", 409));
      await loading;
      Assert.Equal("Reload needed", vm.Status); Assert.True(vm.CanEdit); Assert.False(vm.InWaiting);
   }

   [Fact]
   public async Task TestsUseSavedServerActionsAndPreserveSettingsDraft() {
      var service = new StubService(); var vm = new SmtpManagerVm(service);
      await vm.ReloadAsync(); vm.Settings.Host = "unsaved.example.com";
      await vm.TestConnectionCommand(); Assert.Equal(1, service.ConnectionTests); Assert.Null(service.LastSave);
      Assert.Equal("unsaved.example.com", vm.Settings.Host);
      vm.TestRecipient = "  recipient@example.com  ";
      Assert.True(vm.Commands[nameof(vm.SendTestCommand)]!.CanExecute());
      await vm.SendTestCommand(); Assert.Equal("recipient@example.com", service.Recipient);
      Assert.Contains("accepted", vm.TestStatus);
   }

   private sealed class StubService : ISmtpService
   {
      public SmtpSettingsSave? LastSave { get; private set; }
      public int Reads { get; private set; }
      public int ConnectionTests { get; private set; }
      public string? Recipient { get; private set; }
      public TaskCompletionSource<SmtpSettingsDetail>? Pending { get; set; }
      private static SmtpSettingsDetail Detail(long revision) => new() { Revision = revision, HasPassword = true, Settings = new() { Host = "smtp.example.com" } };
      public Task<SmtpSettingsDetail> GetMeta_SmtpSettings() { Reads++; return Pending?.Task ?? Task.FromResult(Detail(7)); }
      public Task<SmtpSettingsDetail> PostGetMeta_SmtpSettingsSave(SmtpSettingsSave request) { LastSave = request; return Task.FromResult(Detail(request.ExpectedRevision + 1)); }
      public Task<SmtpConnectionResult> PostGetMeta_SmtpTestConnection() { ConnectionTests++; return Task.FromResult(new SmtpConnectionResult { IsSecure = true, CheckedAtUtc = DateTime.UtcNow }); }
      public Task<SmtpSendResult> PostGetMeta_SmtpTestEmail(string recipient) { Recipient = recipient; return Task.FromResult(new SmtpSendResult { MessageId = "test", AcceptedAtUtc = DateTime.UtcNow }); }
      public Task<SmtpSendResult> PostGetMeta_SmtpSend(SmtpMessage message) => throw new NotImplementedException();
      public Task<SmtpSendResult> SendAsync(SmtpMessage message) => throw new NotImplementedException();
   }
}
