using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations
{
   // The DEPLOY card in the container detail: the deploy target of the selected container, its last run, and the
   // Deploy, Configure and History actions. The card reads and acts on its own (IsDeployBusy), so the rest of the
   // screen stays usable while a deploy runs; it never takes the screen-wide busy flag.
   public partial class ContainerManagerVm
   {
      private int _deployVersion;

      private void RegisterDeployCommands() {
         RegisterCommand(nameof(RefreshDeployCommand), RefreshDeployCommand, RefreshDeployCommandAllowed);
         RegisterCommand(nameof(ConfigureDeployCommand), ConfigureDeployCommand, DeployActionAllowed);
         RegisterCommand(nameof(DeployNowCommand), DeployNowCommand, DeployNowCommandAllowed);
         RegisterCommand(nameof(DeployHistoryCommand), DeployHistoryCommand, DeployNowCommandAllowed);
         RegisterCommand(nameof(CopyDeployHostCommand), CopyDeployHostCommand, () => DeployHostCaption.Length > 0);
      }

      #region Data

      /// <summary>Deploy target of the selected container; <c>null</c> when it has none or it is not read yet.</summary>
      public CtnDeployTargetInfo? DeployTarget {
         get => Get<CtnDeployTargetInfo?>();
         private set => Set(value, _ => NotifyDeployChanged());
      }

      /// <summary><c>true</c> while the card reads or a deploy dialog it opened is working.</summary>
      public bool IsDeployBusy {
         get => Get<bool>();
         private set => Set(value, _ => NotifyDeployChanged());
      }

      /// <summary><c>true</c> once the target of the selected container was read (with or without a target).</summary>
      public bool IsDeployLoaded {
         get => Get<bool>();
         private set => Set(value, _ => NotifyDeployChanged());
      }

      /// <summary>Why the card could not be read; empty when it could.</summary>
      public string DeployError {
         get => Get<string>() ?? "";
         private set => Set(value, _ => NotifyDeployChanged());
      }

      public bool HasDeployError => DeployError.Length > 0;
      public bool HasDeployTarget => DeployTarget is not null;
      public bool ShowDeployEmpty => IsDeployLoaded && DeployTarget is null && !HasDeployError;
      public bool ShowDeployLoading => IsDeployBusy && !IsDeployLoaded;

      /// <summary>E.g. <c>SSH · Stack</c>.</summary>
      public string DeployKindCaption => DeployTarget is { } t
         ? $"{(t.Kind == CtnDeployKind.Ssh ? "SSH" : "Portainer")} · {(t.Mode == CtnDeployMode.Stack ? "Stack" : "Container")}"
         : "";

      /// <summary>Address of the Docker server: <c>user@host:port</c> for SSH, the URL for Portainer. Copied in full.</summary>
      public string DeployHostCaption => DeployTarget is { } t
         ? t.Kind == CtnDeployKind.Ssh ? $"{t.User}@{t.Host}:{t.Port ?? 22}" : t.Host
         : "";

      /// <summary>What is recreated: the stack and service, or the container.</summary>
      public string DeployWhereCaption => DeployTarget is not { } t
         ? ""
         : t.Mode == CtnDeployMode.Container
            ? Or(t.Container, "container not set") + (t.Kind == CtnDeployKind.Portainer ? $" · environment {t.EndpointId?.ToString() ?? "not set"}" : "")
            : $"{Or(t.Stack, "stack not set")} · service {Or(t.Service, "not set")}";

      public string DeployFilterCaption => DeployTarget is { } t ? Or(t.TagFilter, "all tags") : "";

      /// <summary>Whether a push deploys on its own.</summary>
      public string DeployAutoCaption => DeployTarget is { IsActive: true } ? "On after push" : "Off (manual only)";

      public bool HasDeployRun => DeployTarget?.LastRun is not null;
      public bool LastDeploySucceeded => DeployTarget?.LastRun?.Result == CtnDeployResult.Success;
      public bool LastDeployFailed => DeployTarget?.LastRun?.Result == CtnDeployResult.Failed;
      public bool LastDeployRunning => DeployTarget?.LastRun?.Result == CtnDeployResult.Running;

      /// <summary>Last run in one line: result, tag or digest, time and user.</summary>
      public string LastDeployCaption => DeployTarget?.LastRun is not { } run
         ? "Never deployed"
         : $"{run.Result} · {run.Tag ?? CtnInput.ShortDigest(run.Digest)} · {LocalTime(run.Started)}{(run.By is { } by ? " · " + by : "")}";

      private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

      private void NotifyDeployChanged() {
         foreach (var name in new[] {
                     nameof(HasDeployError), nameof(HasDeployTarget), nameof(ShowDeployEmpty), nameof(ShowDeployLoading),
                     nameof(DeployKindCaption), nameof(DeployHostCaption), nameof(DeployWhereCaption), nameof(DeployFilterCaption),
                     nameof(DeployAutoCaption), nameof(HasDeployRun), nameof(LastDeploySucceeded), nameof(LastDeployFailed),
                     nameof(LastDeployRunning), nameof(LastDeployCaption)
                  }) {
            NotifyChanged(name);
         }

         RaiseCommandsChanged();
      }

      #endregion

      #region Commands

      /// <summary>Reads the card again, on its own.</summary>
      public Task RefreshDeployCommand() => SelectedNode is { IsFolder: false } node ? LoadDeployAsync(node) : Task.CompletedTask;

      /// <summary>Allowed after a failure too, so a read can be retried; never twice at once.</summary>
      public bool RefreshDeployCommandAllowed() => SelectedNode is { IsFolder: false } && !IsDeployBusy && IsRegistryAvailable;

      private bool DeployActionAllowed() => SelectedNode is { IsFolder: false } && IsDeployLoaded && !IsDeployBusy && !HasDeployError && CanAct;

      /// <summary>Only for a container that has a target.</summary>
      public bool DeployNowCommandAllowed() => DeployActionAllowed() && DeployTarget is not null;

      /// <summary>Opens the target dialog; reads the card again when something was saved or deleted.</summary>
      public async Task ConfigureDeployCommand() {
         if (SelectedNode is not { Image: { } image } node) return;

         var dialog = new CtnDeployTargetDialog(Service, image, DeployTarget, RegistryHost) { Owner = DialogOwner };
         IsDeployBusy = true;
         try {
            dialog.ShowDialog();
         }
         finally {
            IsDeployBusy = false;
         }

         if (dialog.Vm.Changed) await LoadDeployAsync(node);
      }

      /// <summary>Picks a manifest and deploys it now.</summary>
      public async Task DeployNowCommand() {
         if (SelectedNode is not { Image: { } image } node || DeployTarget is not { } target) return;

         var dialog = new CtnDeployRunDialog(Service, image, target, [.. Manifests.Select(m => m.Info)]) { Owner = DialogOwner };
         IsDeployBusy = true;
         try {
            dialog.ShowDialog();
         }
         finally {
            IsDeployBusy = false;
         }

         if (dialog.Vm.HasRun) await LoadDeployAsync(node);
      }

      /// <summary>Shows the run history, with rollback.</summary>
      public async Task DeployHistoryCommand() {
         if (SelectedNode is not { Image: { } image } node) return;

         var dialog = new CtnDeployHistoryDialog(Service, image) { Owner = DialogOwner };
         IsDeployBusy = true;
         try {
            dialog.ShowDialog();
         }
         finally {
            IsDeployBusy = false;
         }

         if (dialog.Vm.HasRun) await LoadDeployAsync(node);
      }

      /// <summary>Copies the full address of the Docker server.</summary>
      public void CopyDeployHostCommand() => CopyToClipboard(DeployHostCaption);

      #endregion

      #region Reading

      private void ClearDeploy() {
         ++_deployVersion;
         IsDeployBusy = false;
         DeployTarget = null;
         DeployError = "";
         IsDeployLoaded = false;
      }

      // Like the manifests: numbered, so an answer for a container that is no longer selected is dropped.
      private async Task LoadDeployAsync(CtnTreeNode node) {
         var version = ++_deployVersion;
         IsDeployBusy = true;
         DeployError = "";
         try {
            var target = await Service.GetMeta_CtnDeployTarget(node.Id);
            if (version != _deployVersion) return;

            DeployTarget = target;
            IsDeployLoaded = true;
         }
         catch (Exception x) {
            if (version != _deployVersion) return;

            DeployTarget = null;
            IsDeployLoaded = true;
            DeployError = x is ActionException action ? ServerMessage(action) : x.Message;
         }
         finally {
            if (version == _deployVersion) IsDeployBusy = false;
         }
      }

      #endregion
   }
}
