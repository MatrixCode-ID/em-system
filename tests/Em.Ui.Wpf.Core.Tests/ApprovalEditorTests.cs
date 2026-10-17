using Em.Api.Core.Models;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class ApprovalEditorTests {
   [Theory]
   [InlineData(true)]
   [InlineData(false)]
   public void ConfirmationEnforcesReasonWithoutChangingInput(bool required) {
      var vm = new ApprovalDecisionDialogVm("Decide", "Context", required);
      var confirmed = 0;
      vm.Confirmed += () => confirmed++;
      vm.Note = "  ";
      Assert.Equal(!required, vm.ConfirmCommandAllowed());
      vm.ConfirmCommand();
      Assert.Equal(required ? 0 : 1, confirmed);
      vm.Note = "  Reviewed  ";
      Assert.True(vm.Commands[nameof(vm.ConfirmCommand)]!.CanExecute());
      vm.ConfirmCommand();
      Assert.Equal(required ? 1 : 2, confirmed);
      Assert.Equal("  Reviewed  ", vm.Note);
   }

   [Fact]
   public void SignerDraftPromotesUsersAndDynamicWithoutDuplicateBackups() {
      var vm = new ApprovalSignerSettingsVm();
      var step = new ApprovalSignerStepVm(vm, new ApprovalSystemStep(1, "Review"));
      var first = new ApprovalPoolItem("first", "First User", true);
      var second = new ApprovalPoolItem("second", "Second User", true);
      vm.DropSignerCommand(new(step, first));
      vm.DropSignerCommand(new(step, second));
      vm.DropSignerCommand(new(step, second));
      Assert.Equal(first, step.Primary!.User);
      Assert.Single(step.Backups);
      vm.MakeSignerCommand(step.Backups[0]);
      Assert.Equal(second, step.Primary!.User);
      Assert.Equal(first, Assert.Single(step.Backups).User);
      vm.DropSignerCommand(new(step, vm.DynamicSigner));
      Assert.True(step.Primary!.User.Dynamic);
      Assert.Equal(2, step.Backups.Count);
      vm.MakeSignerCommand(step.Backups[0]);
      Assert.DoesNotContain(step.Backups, x => x.User.Dynamic);
      Assert.Equal(2, step.Candidates.Count);
      vm.RemoveCommand(step.Primary!);
      Assert.Null(step.Primary);
      Assert.Single(step.Candidates);
   }

   [Fact]
   public void InactivePoolUserCannotBeAssigned() {
      var vm = new ApprovalSignerSettingsVm();
      var step = new ApprovalSignerStepVm(vm, new ApprovalSystemStep(1, "Review"));
      var inactive = new ApprovalPoolItem("old", "Old User", false);
      Assert.False(vm.DropSignerCommandAllowed(new(step, inactive)));
      vm.DropSignerCommand(new(step, inactive));
      Assert.Null(step.Primary);
      Assert.Empty(step.Backups);
      Assert.Equal("DS", vm.DynamicSigner.Initials);
      Assert.Equal("OU", inactive.Initials);
   }
}
