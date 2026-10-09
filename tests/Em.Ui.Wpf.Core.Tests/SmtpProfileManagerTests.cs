using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core.Tests;

public class SmtpProfileManagerTests
{
   [Fact]
   public async Task SuccessfulTestsRememberAddressesAndClearRetainsInputs() {
      using var registry = new SmtpEmailHistoryTests.TestRegistry();
      var history = new Em.Ui.Wpf.Core.SmtpEmailHistory(registry.Open);
      history.Remember("old@example.com", "old-to@example.com");
      var service = new Profiles(); var vm = new SmtpManagerVm((ISmtpProfileService)service);
      vm.InitializeEmailHistory(history); Assert.Single(vm.FromHistory); Assert.Single(vm.ToHistory);
      await vm.ReloadAsync(); vm.TestProfile = vm.Profiles[0];
      vm.TestSender = " new@example.com "; vm.TestRecipient = " new-to@example.com ";
      service.PendingSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
      var sending = vm.SendTestCommand();
      service.PendingSend.SetException(new ActionException("Rejected", 502)); await sending;
      Assert.Single(history.Read().From); Assert.Single(vm.FromHistory);
      service.PendingSend = null; await vm.SendTestCommand();
      Assert.Equal("new@example.com", vm.FromHistory[0]); Assert.Equal("new-to@example.com", vm.ToHistory[0]);
      Assert.Equal(2, history.Read().From.Length); Assert.Contains("accepted", vm.TestStatus);
      vm.ClearHistoryCommand(); Assert.Empty(history.Read().From); Assert.Empty(vm.FromHistory); Assert.Empty(vm.ToHistory);
      Assert.Equal(" new@example.com ", vm.TestSender); Assert.Equal(" new-to@example.com ", vm.TestRecipient);
      Assert.Equal("first", vm.TestProfile!.Id);
   }

   [Fact]
   public async Task TestToolbarUsesItsOwnProfileAndPreservesChoiceAcrossReload() {
      var service = new Profiles(); var vm = new SmtpManagerVm((ISmtpProfileService)service);
      await vm.ReloadAsync(); vm.NewCommand(); vm.ProfileName = "secondary"; await vm.SaveCommand();
      vm.TestSender = "sender@example.com"; vm.TestRecipient = "to@example.com";
      Assert.False(vm.CanSendTest);
      vm.TestProfile = vm.Profiles.Single(p => p.Id == "second");
      vm.SelectedProfile = vm.Profiles.Single(p => p.Id == "first");
      await vm.SendTestCommand(); Assert.Equal("second", service.TestId);
      Assert.True(vm.Profiles.Single(p => p.Id == "first").IsDefault);
      await vm.ReloadAsync(); Assert.Equal("second", vm.TestProfile!.Id); Assert.True(vm.CanSendTest);
      vm.EditCommand(); Assert.False(vm.CanSendTest); vm.CancelCommand();
      vm.SelectedProfile = vm.TestProfile; vm.ConfirmDelete = _ => true; await vm.DeleteCommand();
      Assert.Null(vm.TestProfile); Assert.False(vm.CanSendTest);
      Assert.Equal("sender@example.com", vm.TestSender); Assert.Equal("to@example.com", vm.TestRecipient);
   }

   [Fact]
   public async Task TestToolbarBlocksDuplicateSendsAndRetainsInputsAfterFailure() {
      var service = new Profiles(); var vm = new SmtpManagerVm((ISmtpProfileService)service);
      await vm.ReloadAsync(); vm.TestProfile = vm.Profiles[0];
      vm.TestSender = "sender@example.com"; vm.TestRecipient = "to@example.com";
      service.PendingSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
      var sending = vm.SendTestCommand(); Assert.False(vm.CanSendTest); Assert.False(vm.CanBrowse);
      await vm.SendTestCommand(); Assert.Equal(1, service.SendCalls);
      service.PendingSend.SetException(new ActionException("SMTP rejected the test sender.", 502));
      await sending; Assert.True(vm.CanSendTest); Assert.Contains("rejected", vm.TestStatus);
      Assert.Equal("first", vm.TestProfile!.Id); Assert.Equal("sender@example.com", vm.TestSender);
      Assert.Equal("to@example.com", vm.TestRecipient);
   }

   [Fact]
   public async Task SaveEnabledRelayWithoutSenderPopulatesInitiallyEmptyTable() {
      var service = new Profiles(); var vm = new SmtpManagerVm((ISmtpProfileService)service);
      await vm.ReloadAsync(); vm.SelectedProfile = vm.Profiles[0]; vm.ConfirmDelete = _ => true;
      await vm.DeleteCommand(); Assert.Empty(vm.Profiles);
      vm.NewCommand(); vm.ProfileName = "relay";
      vm.Settings.Enabled = true; vm.Settings.Authenticate = false;
      vm.Settings.Host = "relay.example.com"; vm.Settings.Port = 25; vm.Settings.Security = SmtpSecurity.None;
      await vm.SaveCommand();
      Assert.Equal("", service.LastSave!.Settings.FromAddress);
      Assert.True(service.LastSave.Settings.Enabled);
      Assert.Equal("relay", Assert.Single(vm.Profiles).Name);
      Assert.Same(vm.Profiles[0], vm.SelectedProfile); Assert.False(vm.IsEditorOpen);
      Assert.Contains("saved", vm.Status);
   }

   [Fact]
   public async Task NewRemainsAvailableWithDefaultAndEditorDoesNotMutateSavedRows() {
      var service = new Profiles(); var vm = new SmtpManagerVm((ISmtpProfileService)service);
      await vm.ReloadAsync(); Assert.Single(vm.Profiles); Assert.True(vm.Commands[nameof(vm.NewCommand)]!.CanExecute());
      vm.NewCommand(); vm.ProfileName = "secondary"; vm.NewPassword = "draft-secret";
      Assert.True(vm.IsEditorOpen); Assert.False(vm.CanBrowse);
      await vm.SaveCommand(); Assert.Equal(2, vm.Profiles.Count); Assert.Null(vm.NewPassword); Assert.False(vm.IsEditorOpen);
      Assert.Equal("draft-secret", service.LastSave!.Password); Assert.True(vm.Commands[nameof(vm.NewCommand)]!.CanExecute());
      vm.EditCommand(); vm.Settings.Host = "draft.example.com";
      Assert.Equal("smtp.example.com", vm.SelectedProfile!.Settings.Host);
      vm.CancelCommand(); Assert.False(vm.IsEditorOpen);
      await vm.SetDefaultCommand(); Assert.True(vm.SelectedProfile!.IsDefault);
      await vm.ClearDefaultCommand(); Assert.Contains("No default", vm.DefaultStatus);
   }

   [Fact]
   public async Task SelectedProfileTestsAndBusyRecoveryDoNotChangeDefaultOrLoseDraft() {
      var service = new Profiles(); var vm = new SmtpManagerVm((ISmtpProfileService)service);
      await vm.ReloadAsync(); vm.SelectedProfile = vm.Profiles[0];
      await vm.TestConnectionCommand(); Assert.Equal("first", service.TestId);
      vm.TestProfile = vm.Profiles[0]; vm.TestRecipient = " to@example.com ";
      Assert.False(vm.Commands[nameof(vm.SendTestCommand)]!.CanExecute());
      vm.TestSender = " sender@example.com ";
      Assert.True(vm.Commands[nameof(vm.SendTestCommand)]!.CanExecute()); await vm.SendTestCommand();
      Assert.Equal("first", service.TestId); Assert.Equal("sender@example.com", service.Sender); Assert.Equal("to@example.com", service.Recipient);
      vm.EditCommand(); vm.NewPassword = "keep-draft"; service.Conflict = true;
      await vm.SaveCommand(); Assert.True(vm.IsEditorOpen); Assert.Equal("keep-draft", vm.NewPassword);
      Assert.Contains("Reload", vm.Status); Assert.False(vm.IsBusy);
      await vm.ReloadAsync(); Assert.Null(vm.NewPassword); Assert.False(vm.IsEditorOpen);
      vm.SelectedProfile = vm.Profiles[0]; vm.ConfirmDelete = _ => false; await vm.DeleteCommand(); Assert.Single(vm.Profiles);
      vm.ConfirmDelete = _ => true; await vm.DeleteCommand(); Assert.Empty(vm.Profiles);
      Assert.True(vm.Commands[nameof(vm.NewCommand)]!.CanExecute());
   }

   private sealed class Profiles : ISmtpProfileService
   {
      private readonly List<SmtpProfileDetail> rows = [new() { Id = "first", Name = "primary", IsDefault = true, Settings = new() { Host = "smtp.example.com" } }];
      private long revision = 1;
      public SmtpProfileSave? LastSave; public bool Conflict; public string? TestId; public string? Recipient; public string? Sender;
      public TaskCompletionSource<SmtpSendResult>? PendingSend; public int SendCalls;
      private SmtpProfileList List() => new() { Revision = revision, Profiles = rows.ToArray() };
      public Task<SmtpProfileList> GetMeta_SmtpProfiles() => Task.FromResult(List());
      public Task<SmtpProfileDetail> PostGetMeta_SmtpProfileSave(SmtpProfileSave request) {
         if (Conflict) throw new ActionException("Reload before saving", 409);
         LastSave = request; var row = new SmtpProfileDetail { Id = request.Id ?? "second", Name = request.Name,
            Revision = ++revision, Settings = new() { Host = "smtp.example.com" } };
         rows.RemoveAll(x => x.Id == row.Id); rows.Add(row); return Task.FromResult(row);
      }
      public Task<SmtpProfileList> PostGetMeta_SmtpProfileDelete(string id, long expectedRevision) { rows.RemoveAll(x => x.Id == id); revision++; return Task.FromResult(List()); }
      public Task<SmtpProfileList> PostGetMeta_SmtpDefault(string? id, long expectedRevision) { foreach (var row in rows) row.IsDefault = row.Id == id; revision++; return Task.FromResult(List()); }
      public Task<SmtpConnectionResult> PostGetMeta_SmtpProfileTestConnection(string id) { TestId = id; return Task.FromResult(new SmtpConnectionResult()); }
      public Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmail(string id, string recipient) { TestId = id; Recipient = recipient; return Task.FromResult(new SmtpSendResult()); }
      public Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmailFrom(string id, string sender, string recipient) { SendCalls++; TestId = id; Sender = sender; Recipient = recipient; return PendingSend?.Task ?? Task.FromResult(new SmtpSendResult()); }
      public Task<SmtpSendResult> PostGetMeta_SmtpSendByName(string? name, SmtpMessage message) => throw new NotImplementedException();
      public Task<SmtpSendResult> SendByNameAsync(string? name, SmtpMessage message) => throw new NotImplementedException();
   }
}
