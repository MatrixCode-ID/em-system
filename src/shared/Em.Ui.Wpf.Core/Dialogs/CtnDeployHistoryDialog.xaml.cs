using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Deploy history of one container, latest first, with the step log of the selected run and Rollback to the digest of
   /// an earlier successful run. <see cref="CtnDeployHistoryDialogVm.HasRun"/> tells the caller to read the card again.
   /// </summary>
   public partial class CtnDeployHistoryDialog : EmWindow
   {
      /// <summary>Creates a new instance of <see cref="CtnDeployHistoryDialog"/>.</summary>
      public CtnDeployHistoryDialog(ICtnServices service, CtnImageInfo image) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(service, image);
         Loaded += async (_, _) => await Vm.RefreshCommand();
      }

      /// <summary>ViewModel of this dialog.</summary>
      public CtnDeployHistoryDialogVm Vm => (CtnDeployHistoryDialogVm)DataContext;
   }

   /// <summary>ViewModel for <see cref="CtnDeployHistoryDialog"/>.</summary>
   public class CtnDeployHistoryDialogVm : MvvmModelBase
   {
      private const int Take = 50;
      private ICtnServices? _service;
      private CtnImageInfo? _image;

      /// <summary>Creates a new instance of <see cref="CtnDeployHistoryDialogVm"/>.</summary>
      public CtnDeployHistoryDialogVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, () => IsNotBusy);
         RegisterCommand(nameof(RollbackCommand), RollbackCommand, () => IsNotBusy && SelectedRun is { Info.CanRollback: true });
      }

      internal void Initialize(ICtnServices service, CtnImageInfo image) {
         _service = service;
         _image = image;
         NotifyChanged(nameof(Title));
      }

      private ICtnServices Api => _service ?? throw new InvalidOperationException("The dialog is not initialized.");

      /// <summary>The title.</summary>
      public string Title => _image is null ? "Deploy history" : $"Deploy history of {_image.FullName}";

      /// <summary>The runs.</summary>
      public CtnDeployRunItem[] Runs {
         get => Get<CtnDeployRunItem[]>() ?? [];
         private set => Set(value, _ => NotifyChanged(nameof(IsEmpty)));
      }

      /// <summary>Indicates empty.</summary>
      public bool IsEmpty => Runs.Length == 0 && ErrorText.Length == 0;

      /// <summary>The selected run.</summary>
      public CtnDeployRunItem? SelectedRun {
         get => Get<CtnDeployRunItem?>();
         set => Set(value, _ => {
            NotifyChanged(nameof(DetailText));
            RaiseCommandsChanged();
         });
      }

      /// <summary>Step log of the selected run, plus the previous digest and any rewritten file note.</summary>
      public string DetailText => SelectedRun?.Detail ?? "";

      /// <summary>The error text.</summary>
      public string ErrorText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasError))); }
      /// <summary>Indicates there is error.</summary>
      public bool HasError => ErrorText.Length > 0;

      /// <summary><c>true</c> after a rollback ran.</summary>
      public bool HasRun { get; private set; }

      /// <summary>Reads the history again, keeping the selected run when it is still listed.</summary>
      public async Task RefreshCommand() {
         if (_image is null) return;
         var keep = SelectedRun?.Info.Id;
         await RunAsync("Loading...", async () => {
            Runs = [.. (await Api.GetMeta_CtnDeployRuns(_image.Id, Take)).Select(r => new CtnDeployRunItem(r))];
            SelectedRun = Runs.FirstOrDefault(r => r.Info.Id == keep) ?? Runs.FirstOrDefault();
         });
      }

      /// <summary>Deploys the digest of the selected successful run again, after confirmation.</summary>
      public async Task RollbackCommand() {
         if (_image is null || SelectedRun is not { Info.CanRollback: true } run || MainWindow is not { } owner) return;
         if (owner.ShowMboxDecideWarning(
                $"Roll {_image.FullName} back to {run.Caption} ({run.ShortDigest}), deployed {run.StartedCaption}?\n\n" +
                "The container is recreated with that image; it is briefly unavailable.", "Rollback") != MessageBoxResult.Yes) {
            return;
         }

         CtnDeployRunInfo? result = null;
         await RunAsync("Rolling back... this can take several minutes.", async () => {
            result = await Api.PostGetMeta_CtnDeployRollback(_image.Id, run.Info.Id);
            HasRun = true;
         });

         if (result is null) return;
         await RefreshCommand();
         SelectedRun = Runs.FirstOrDefault(r => r.Info.Id == result.Id) ?? SelectedRun;
         // Set after the refresh, which clears the error line when it starts.
         if (result.Result != CtnDeployResult.Success) ErrorText = "Rollback failed; see its output.";
      }

      private async Task RunAsync(string waiterText, Func<Task> work) {
         if (IsBusy) return;
         ErrorText = "";
         WaiterText = waiterText;
         IsBusy = InWaiting = true;
         RaiseCommandsChanged();
         try {
            await work();
         }
         catch (ActionException x) {
            ErrorText = ContainerManagerVm.ServerMessage(x);
         }
         catch (TimeoutException x) {
            ErrorText = x.Message;
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            IsBusy = InWaiting = false;
            NotifyChanged(nameof(IsEmpty));
            RaiseCommandsChanged();
         }
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }
   }

   /// <summary>One run in the history list.</summary>
   public class CtnDeployRunItem(CtnDeployRunInfo info)
   {
      /// <summary>The info.</summary>
      public CtnDeployRunInfo Info { get; } = info;
      /// <summary>The caption.</summary>
      public string Caption => Info.Tag ?? ShortDigest;
      /// <summary>The short digest.</summary>
      public string ShortDigest => CtnInput.ShortDigest(Info.Digest);
      /// <summary>The started caption.</summary>
      public string StartedCaption => ContainerManagerVm.LocalTime(Info.Started);
      /// <summary>The trigger caption.</summary>
      public string TriggerCaption => Info.Trigger switch {
         CtnDeployTrigger.AfterPush => "after push",
         CtnDeployTrigger.Rollback => "rollback",
         _ => "manual"
      };

      /// <summary>The meta caption.</summary>
      public string MetaCaption => $"{StartedCaption} · {TriggerCaption}{(Info.By is { } by ? " · " + by : "")}";
      /// <summary>The result caption.</summary>
      public string ResultCaption => Info.Result.ToString();
      /// <summary>Indicates success.</summary>
      public bool IsSuccess => Info.Result == CtnDeployResult.Success;
      /// <summary>Indicates failed.</summary>
      public bool IsFailed => Info.Result == CtnDeployResult.Failed;
      /// <summary>Indicates running.</summary>
      public bool IsRunning => Info.Result == CtnDeployResult.Running;

      /// <summary>The detail.</summary>
      public string Detail =>
         $"Digest: {Info.Digest}\n" +
         (Info.PrevDigest is { } previous ? $"Previous: {previous}\n" : "") +
         (Info.Finished is { } finished ? $"Finished: {ContainerManagerVm.LocalTime(finished)}\n" : "") +
         "\n" + (Info.Output ?? "");
   }
}
