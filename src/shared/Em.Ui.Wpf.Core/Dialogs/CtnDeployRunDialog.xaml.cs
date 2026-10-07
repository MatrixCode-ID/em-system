using System.Text.RegularExpressions;
using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Manual deploy: pick a manifest of the container (by default the newest one with a tag the target's filter
   /// accepts) and deploy it now, ignoring the filter. Shows the server's step log. <see cref="CtnDeployRunDialogVm.HasRun"/>
   /// tells the caller to read the card again.
   /// </summary>
   public partial class CtnDeployRunDialog : EmWindow
   {
      /// <summary>Creates a new instance of <see cref="CtnDeployRunDialog"/>.</summary>
      public CtnDeployRunDialog(ICtnServices service, CtnImageInfo image, CtnDeployTargetInfo target, CtnManifestInfo[] manifests) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(service, image, target, manifests);
      }

      /// <summary>ViewModel of this dialog.</summary>
      public CtnDeployRunDialogVm Vm => (CtnDeployRunDialogVm)DataContext;
   }

   /// <summary>ViewModel for <see cref="CtnDeployRunDialog"/>.</summary>
   public class CtnDeployRunDialogVm : MvvmModelBase
   {
      private ICtnServices? _service;
      private CtnImageInfo? _image;
      private CtnDeployTargetInfo? _target;

      /// <summary>Creates a new instance of <see cref="CtnDeployRunDialogVm"/>.</summary>
      public CtnDeployRunDialogVm() {
         RegisterCommand(nameof(DeployCommand), DeployCommand, () => IsNotBusy && SelectedManifest is not null);
      }

      internal void Initialize(ICtnServices service, CtnImageInfo image, CtnDeployTargetInfo target, CtnManifestInfo[] manifests) {
         _service = service;
         _image = image;
         _target = target;
         Manifests = [.. manifests.Select(m => new CtnDeployManifestItem(m))];
         SelectedManifest = Manifests.FirstOrDefault(m => m.Info.Tags.Any(t => MatchesFilter(target.TagFilter, t))) ?? Manifests.FirstOrDefault();
         NotifyChanged(nameof(Title));
         NotifyChanged(nameof(TargetCaption));
         NotifyChanged(nameof(HasNoManifests));
      }

      private ICtnServices Api => _service ?? throw new InvalidOperationException("The dialog is not initialized.");

      /// <summary>The title.</summary>
      public string Title => _image is null ? "Deploy" : $"Deploy {_image.FullName}";

      /// <summary>The target caption.</summary>
      public string TargetCaption => _target is not { } t
         ? ""
         : $"To {(t.Kind == CtnDeployKind.Ssh ? $"{t.User}@{t.Host}" : t.Host)} · " +
           (t.Mode == CtnDeployMode.Stack ? $"{t.Stack} / {t.Service}" : $"container {t.Container}") +
           ". The tag filter is ignored here.";

      /// <summary>The manifests.</summary>
      public CtnDeployManifestItem[] Manifests { get => Get<CtnDeployManifestItem[]>() ?? []; private set => Set(value); }

      /// <summary>Indicates there is no manifests.</summary>
      public bool HasNoManifests => Manifests.Length == 0;

      /// <summary>The selected manifest.</summary>
      public CtnDeployManifestItem? SelectedManifest {
         get => Get<CtnDeployManifestItem?>();
         set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>The output text.</summary>
      public string OutputText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasOutput))); }
      /// <summary>Indicates there is output.</summary>
      public bool HasOutput => OutputText.Length > 0;

      /// <summary>The result text.</summary>
      public string ResultText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasResult))); }
      /// <summary>Indicates there is result.</summary>
      public bool HasResult => ResultText.Length > 0;

      /// <summary>Indicates failed.</summary>
      public bool Failed { get => Get<bool>(); private set => Set(value); }

      /// <summary><c>true</c> after a deploy ran, whatever its result.</summary>
      public bool HasRun { get; private set; }

      /// <summary>Deploys the selected manifest after confirmation.</summary>
      public async Task DeployCommand() {
         if (_image is null || SelectedManifest is not { } manifest || MainWindow is not { } owner || IsBusy) return;
         if (owner.ShowMboxDecideWarning($"Deploy {manifest.Caption} of {_image.FullName} now?\n\n{TargetCaption}\n\n" +
                                          "The container is recreated; it is briefly unavailable.", "Deploy") != MessageBoxResult.Yes) {
            return;
         }

         ResultText = OutputText = "";
         WaiterText = "Deploying... this can take several minutes.";
         IsBusy = InWaiting = true;
         RaiseCommandsChanged();
         try {
            var run = await Api.PostGetMeta_CtnDeployRun(_image.Id, manifest.Info.Digest, manifest.Info.Tags.FirstOrDefault());
            HasRun = true;
            Failed = run.Result != CtnDeployResult.Success;
            ResultText = Failed ? "Deploy failed. Fix the server and deploy again." : "Deployed.";
            OutputText = run.Output ?? "";
         }
         catch (ActionException x) {
            Failed = true;
            ResultText = ContainerManagerVm.ServerMessage(x);
         }
         catch (TimeoutException x) {
            HasRun = Failed = true;
            ResultText = x.Message;
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseCommandsChanged();
         }
      }

      // Same rules as the server's tag filter: comma-separated, * and ?, case-sensitive; empty matches all.
      internal static bool MatchesFilter(string? filter, string tag) {
         var patterns = (filter ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
         return patterns.Length == 0 || patterns.Any(p =>
            Regex.IsMatch(tag, "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.CultureInvariant));
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }
   }

   /// <summary>One manifest to pick in the deploy dialog.</summary>
   public class CtnDeployManifestItem(CtnManifestInfo info)
   {
      /// <summary>The info.</summary>
      public CtnManifestInfo Info { get; } = info;
      /// <summary>The short digest.</summary>
      public string ShortDigest => CtnInput.ShortDigest(Info.Digest);
      /// <summary>The tags caption.</summary>
      public string TagsCaption => Info.Tags.Length == 0 ? "untagged" : string.Join(", ", Info.Tags);
      /// <summary>The caption.</summary>
      public string Caption => Info.Tags.FirstOrDefault() ?? ShortDigest;
      /// <summary>The pushed caption.</summary>
      public string PushedCaption => "pushed " + ContainerManagerVm.LocalTime(Info.PushedAt);
   }
}
