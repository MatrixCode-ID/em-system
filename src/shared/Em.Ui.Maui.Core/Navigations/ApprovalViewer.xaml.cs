using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The mobile viewer of one approval request, opened through <c>em.approval.editor</c> with an
   /// <see cref="ApprovalManagerNavigationPayload"/> carrying the request ID. System requests show the view
   /// the module registered through <c>AddSystemApproval</c>; Data requests show their proposed changes.
   /// </summary>
   public partial class ApprovalViewer : ContentView, INavigationBody
   {
      public ApprovalViewer() {
         InitializeComponent();
      }

      /// <summary>The view model of this screen, read back from the BindingContext set in XAML.</summary>
      public ApprovalViewerVm Vm => (ApprovalViewerVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         Vm.Configure(args.Data as ApprovalManagerNavigationPayload);
         return Vm.RefreshAsync();
      }

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.Release();
         return Task.CompletedTask;
      }
   }

   /// <summary>One signer step of a System request.</summary>
   public sealed class ApprovalStepVm : MvvmModelBase
   {
      private readonly ApprovalViewerVm _owner;

      public ApprovalStepVm(ApprovalViewerVm owner, ta_ApprovalRequestStep step) {
         _owner = owner;
         Step = step;
         RegisterCommand(nameof(SelectCommand), SelectCommand);
      }

      public string SelectionCaption => $"{(IsSelected ? "Selected · " : "Select · ")}{Heading} · {Status}";

      /// <summary>The step this card shows.</summary>
      public ta_ApprovalRequestStep Step { get; }

      private ApprovalRequestDetail Detail => _owner.Detail!;

      private ta_ApprovalRequestSignature? Signature =>
         Detail.Signatures.FirstOrDefault(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId);

      /// <summary>"L1 · Prepared By".</summary>
      public string Heading => $"L{Step.cApprovalRequestStepLevel} · {Step.cApprovalRequestStepName}";

      /// <summary>Whether the user may sign or reject this step now.</summary>
      public bool IsMyTurn => Detail.DecidableStepIds.Contains(Step.cApprovalRequestStepId);

      /// <summary>Inverse of <see cref="IsMyTurn"/>, for binding without a converter.</summary>
      public bool IsNotMyTurn => !IsMyTurn;

      private bool IsSigned => Step.cApprovalRequestStepStage == ApprovalStepStatus.Approved;

      private bool IsRejected => Step.cApprovalRequestStepStage == ApprovalStepStatus.Rejected;

      /// <summary>The readable state of the step.</summary>
      public string Status => IsSigned ? "Signed" : IsRejected ? "Rejected" : IsMyTurn ? "Your turn" : "Waiting";

      /// <summary>The state icon of the step.</summary>
      public string Glyph => IsSigned ? FontIcons.CircleCheck : IsRejected ? FontIcons.CircleXmark : IsMyTurn ? FontIcons.PenNib : FontIcons.Clock;

      /// <summary>The primary signer and how many backups stand behind them.</summary>
      public string SignerName {
         get {
            var candidates = Detail.Candidates.Where(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId).ToArray();
            var primary = candidates.FirstOrDefault(x => !x.cApprovalRequestStepCandidateBackup);
            var name = primary is null ? "No signer" : _owner.UserName(primary.cUserId);
            var backups = candidates.Count(x => x.cApprovalRequestStepCandidateBackup);
            return backups > 0 ? $"{name} · {backups} backup{(backups > 1 ? "s" : "")}" : name;
         }
      }

      /// <summary>Who signed and when, with the four-eyes and administrator marks.</summary>
      public string SignatureText => Signature is { } signature
         ? $"{_owner.UserName(signature.cUserId)} · {ApprovalTexts.Time(signature.datestamp)}" +
           (signature.cApprovalRequestSignatureAdmin ? " · admin" : "") +
           (signature.cApprovalRequestSignatureBackup ? " · four-eyes backup" : "") +
           $" · {signature.cApprovalRequestSignatureCode}"
         : string.Empty;

      /// <summary><c>true</c> when the step carries a signature.</summary>
      public bool HasSignature => Signature is not null;

      /// <summary>The note recorded with the signature.</summary>
      public string DecisionNote => Signature?.cApprovalRequestSignatureNote ?? string.Empty;

      /// <summary><c>true</c> when the signature has a note.</summary>
      public bool HasNote => !string.IsNullOrWhiteSpace(DecisionNote);

      /// <summary>Whether the action bar decides this step.</summary>
      public bool IsSelected {
         get => Get(false);
         set => Set(value);
      }

      public void SelectCommand() {
         if (IsMyTurn) _owner.SelectStep(this);
      }
   }

   /// <summary>One proposed field change of a Data request.</summary>
   public sealed record ApprovalFieldChangeVm(ta_ApprovalRequestItemField Row)
   {
      /// <summary>The changed column.</summary>
      public string Column => Row.cApprovalRequestItemFieldName;

      /// <summary>"old → new".</summary>
      public string Change => $"{Row.cApprovalRequestItemFieldOld ?? "(empty)"} → {Row.cApprovalRequestItemFieldNew ?? "(empty)"}";

      /// <summary>Whether the stored value changed after the request was made.</summary>
      public bool Conflict => Row.cApprovalRequestItemFieldConflict;

      /// <summary>The current value that conflicts with the request.</summary>
      public string ConflictText => $"Changed meanwhile to {Row.cApprovalRequestItemFieldCurrent ?? "(empty)"}";
   }

   /// <summary>A comment or recorded decision, in time order.</summary>
   public sealed record ApprovalCommentItemVm(string Author, DateTime When, string Text, string Decision)
   {
      /// <summary>"Name · Signed · 24-Oct-2026 10:15".</summary>
      public string Header => string.IsNullOrEmpty(Decision)
         ? $"{Author} · {ApprovalTexts.Time(When)}"
         : $"{Author} · {Decision} · {ApprovalTexts.Time(When)}";

      /// <summary><c>true</c> when there is text to draw.</summary>
      public bool HasText => !string.IsNullOrWhiteSpace(Text);

      /// <summary>A pen for decisions, a bubble for comments.</summary>
      public string Glyph => string.IsNullOrEmpty(Decision) ? FontIcons.Comment : FontIcons.PenNib;
   }

   /// <summary>View model of <see cref="ApprovalViewer"/>; every action follows the server-returned rights.</summary>
   public sealed class ApprovalViewerVm : MvvmModelBase
   {
      private ApprovalManagerNavigationPayload? _filter;
      private int _page = 1;
      private int _total;
      /// <summary>Applies an explicit request or a waiting-work filter.</summary>
      public void Configure(ApprovalManagerNavigationPayload? payload) { _filter = payload; RequestId = payload?.ApprovalRequestId; _page = 1; }
      /// <summary>Opens the next request in the fallback filter.</summary>
      public Task NextCommand() { _page++; return RefreshAsync(); }
      /// <summary>Opens the previous request in the fallback filter.</summary>
      public Task PreviousCommand() { _page--; return RefreshAsync(); }
      private bool NextCommandAllowed() => !IsBusy && _filter?.ApprovalRequestId is null && _page < _total;
      private bool PreviousCommandAllowed() => !IsBusy && _filter?.ApprovalRequestId is null && _page > 1;
      private IEmApprovalSystemEditor? _editor;
      private bool _released;
      private int _version;

      public ApprovalViewerVm() {
         RegisterCommand(nameof(NextCommand), NextCommand, NextCommandAllowed);
         RegisterCommand(nameof(PreviousCommand), PreviousCommand, PreviousCommandAllowed);
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand(nameof(ApproveCommand), ApproveCommand, ApproveCommandAllowed);
         RegisterCommand(nameof(RejectCommand), RejectCommand, RejectCommandAllowed);
         RegisterCommand(nameof(CommentCommand), CommentCommand, CommentCommandAllowed);
         RegisterCommand(nameof(PreviewCommand), PreviewCommand, PreviewCommandAllowed);
      }

      private IApprovalServices Service => EmApp!.ServiceProvider.GetRequiredService<IApprovalServices>();

      private Page? DialogPage => HostPage ?? EmApp?.RootPage;

      /// <summary>The request shown.</summary>
      public string? RequestId { get; set; }

      /// <summary>The server-authorized state of the request, or <c>null</c> before it is loaded.</summary>
      public ApprovalRequestDetail? Detail { get; private set; }

      /// <summary>The module's document view of a System request.</summary>
      public View? ModuleView {
         get => Get<View?>(null);
         private set => Set(value, _ => NotifyChanged(nameof(HasModuleView)));
      }

      /// <summary><c>true</c> when the module supplied a view.</summary>
      public bool HasModuleView => ModuleView is not null;

      /// <summary>Signer steps of a System request.</summary>
      public ObservableCollection<ApprovalStepVm> Steps { get; } = [];

      /// <summary>Proposed changes of a Data request.</summary>
      public ObservableCollection<ApprovalFieldChangeVm> Fields { get; } = [];

      /// <summary>Comments and decisions in time order.</summary>
      public ObservableCollection<ApprovalCommentItemVm> Comments { get; } = [];

      /// <summary>The step decided by the action bar, for System requests.</summary>
      public ApprovalStepVm? SelectedStep { get; private set; }

      /// <summary>The comment being written.</summary>
      public string Comment {
         get => Get(string.Empty);
         set => Set(value, _ => Commands[nameof(CommentCommand)]?.RaiseCanExecuteChanged());
      }

      /// <summary>Bound to the pull-to-refresh state.</summary>
      public bool IsRefreshing {
         get => Get(false);
         set => Set(value);
      }

      /// <summary>The last failure.</summary>
      public string Error {
         get => Get(string.Empty);
         set => Set(value, _ => NotifyChanged(nameof(HasError)));
      }

      /// <summary><c>true</c> when <see cref="Error"/> holds a message.</summary>
      public bool HasError => !string.IsNullOrEmpty(Error);

      /// <summary>A neutral message from the server, e.g. a decision that was not applied.</summary>
      public string Notice {
         get => Get(string.Empty);
         set => Set(value, _ => NotifyChanged(nameof(HasNotice)));
      }

      /// <summary><c>true</c> when <see cref="Notice"/> holds a message.</summary>
      public bool HasNotice => !string.IsNullOrEmpty(Notice);

      private ta_ApprovalRequest? Header => Detail?.Header;

      /// <summary>The approval name.</summary>
      public string Caption => Header?.cApprovalName.ToUpperInvariant() ?? string.Empty;

      /// <summary>The first summary line.</summary>
      public string Summary => Header?.cSummary ?? string.Empty;

      /// <summary>The second summary line.</summary>
      public string SubSummary => Header?.cSubSummary ?? string.Empty;

      /// <summary><c>true</c> when there is a second summary line.</summary>
      public bool HasSubSummary => !string.IsNullOrWhiteSpace(Header?.cSubSummary);

      /// <summary>The readable stage.</summary>
      public string Status => Header is null ? string.Empty : ApprovalTexts.Stage(Header.cApprovalRequestStage);

      /// <summary>The active level.</summary>
      public string LevelText => Header is null ? string.Empty : $"Level {Header.cApprovalRequestLevel}";

      /// <summary>Requester and creation time.</summary>
      public string RequestedText => Header is null ? string.Empty
         : $"by {UserName(Header.cApprovalRequestRequesterId)} · {ApprovalTexts.Time(Header.datestamp)}";

      /// <summary><c>true</c> for a System request with signer steps.</summary>
      public bool HasSteps => Steps.Count > 0;

      /// <summary><c>true</c> for a Data request with field changes.</summary>
      public bool HasFields => Fields.Count > 0;

      /// <summary><c>true</c> when nothing has been said yet.</summary>
      public bool HasNoComments => Comments.Count == 0;

      /// <summary>Whether the module offers a document preview.</summary>
      public bool HasPreview => _editor?.HasPreview == true;

      private bool IsSystem => Header?.cApprovalRequestKind == ApprovalKind.System;

      /// <summary>Whether the action bar is shown.</summary>
      public bool CanDecide => Detail is { CanDecide: true } && (!IsSystem || SelectedStep is not null);

      /// <summary>What the action bar decides.</summary>
      public string DecisionCaption => IsSystem
         ? SelectedStep is { } step ? $"Your decision for {step.Step.cApprovalRequestStepName}" : string.Empty
         : "Your decision for this request";

      /// <summary>The caption of the positive button.</summary>
      public string ApproveText => IsSystem ? "Sign" : "Approve";

      /// <summary>Resolves a user ID to the name the server sent along.</summary>
      public string UserName(string id) => Detail?.Users.FirstOrDefault(x => x.UserId == id)?.Name ?? id;

      /// <summary>Stops pending loads from touching a released screen.</summary>
      public void Release() {
         _released = true;
         _version++;
         ModuleView = null;
         _editor = null;
      }

      /// <summary>Makes <paramref name="step"/> the one the action bar decides.</summary>
      public void SelectStep(ApprovalStepVm step) {
         foreach (var item in Steps) item.IsSelected = ReferenceEquals(item, step);
         SelectedStep = step;
         RaiseState();
      }

      public Task RefreshCommand() => RefreshAsync();

      public bool RefreshCommandAllowed() => !IsBusy;

      public Task ApproveCommand() => DecideAsync(true);

      public bool ApproveCommandAllowed() => !IsBusy && CanDecide;

      public Task RejectCommand() => DecideAsync(false);

      public bool RejectCommandAllowed() => !IsBusy && CanDecide;

      public Task CommentCommand() => RunAsync(async () => {
         await Service.PostMeta_ApprovalComment(RequestId!, Comment.Trim());
         Comment = string.Empty;
         await LoadAsync();
      });

      public bool CommentCommandAllowed() => !IsBusy && Detail is not null && !string.IsNullOrWhiteSpace(Comment);

      public Task PreviewCommand() => RunAsync(async () => {
         var stream = await _editor!.GetPreviewAsync() ?? throw new InvalidOperationException("The module returned no preview.");
         var folder = Path.Combine(FileSystem.CacheDirectory, "approval-preview");
         Directory.CreateDirectory(folder);
         var path = Path.Combine(folder, $"{RequestId}.pdf");
         await using (stream)
         await using (var file = File.Create(path)) {
            await stream.CopyToAsync(file);
         }

         // The PDF is handed to whatever viewer the device has: the client carries no PDF renderer of its own.
         await Launcher.Default.OpenAsync(new OpenFileRequest(Summary, new ReadOnlyFile(path, "application/pdf")));
      });

      public bool PreviewCommandAllowed() => !IsBusy && HasPreview;

      /// <summary>Reloads the request from the server.</summary>
      public Task RefreshAsync() => RunAsync(LoadAsync);

      // Never throws: it runs from commands, whose async void execution has nothing above it to catch.
      private async Task RunAsync(Func<Task> work) {
         if (EmApp is null || _released) return;
         if (IsBusy) {
            IsRefreshing = false;
            return;
         }

         IsBusy = true;
         Error = string.Empty;
         RaiseState();
         try {
            await work();
         }
         catch (Exception x) {
            Error = x.SerializedMessagesDefault();
         }
         finally {
            IsBusy = false;
            IsRefreshing = false;
            RaiseState();
         }
      }

      private async Task LoadAsync() {
         if (_filter is { ApprovalRequestId: null }) {
            var page = await Service.PostGetMeta_ApprovalRequests(new(_filter.Kind, _filter.Name, WaitingForMeOnly: _filter.WaitingForMeOnly, Page: _page, PageSize: 1, OldestFirst: true));
            _total = page.Total; RequestId = page.Items.FirstOrDefault()?.cApprovalRequestId;
         }
         if (string.IsNullOrEmpty(RequestId)) { Detail = null; ModuleView = null; Steps.Clear(); Comments.Clear(); Error = "No approval requests match the current filters."; RaiseState(); return; }
         var version = ++_version;
         var detail = await Service.GetMeta_ApprovalRequest(RequestId);
         if (_released || version != _version) return;

         Detail = detail;
         Steps.Clear();
         Fields.Clear();
         Comments.Clear();
         SelectedStep = null;

         foreach (var field in detail.Fields) Fields.Add(new ApprovalFieldChangeVm(field));

         var comments = detail.Comments
            .Select(x => new ApprovalCommentItemVm(UserName(x.cUserId), x.datestamp, x.cApprovalRequestCommentText, string.Empty))
            .Concat(detail.Signatures.Select(x => new ApprovalCommentItemVm(UserName(x.cUserId), x.datestamp,
               x.cApprovalRequestSignatureNote ?? string.Empty, x.cApprovalRequestSignatureApproved ? "Signed" : "Rejected")));
         foreach (var comment in comments.OrderBy(x => x.When)) Comments.Add(comment);

         if (detail.Header.cApprovalRequestKind == ApprovalKind.System) {
            foreach (var step in detail.Steps.OrderBy(x => x.cApprovalRequestStepLevel).ThenBy(x => x.cApprovalRequestStepOrder))
               Steps.Add(new ApprovalStepVm(this, step));
            if (Steps.FirstOrDefault(x => x.IsMyTurn) is { } mine) SelectStep(mine);

            // The module view is built once per screen; a refresh only hands it the new state.
            if (_editor is null) {
               var editorType = EmApp!.ServiceProvider.GetRequiredService<ApprovalEditorRegistry>().Find(detail.Header.cApprovalName);
               if (editorType is not null) {
                  var view = (View)ActivatorUtilities.CreateInstance(EmApp.ServiceProvider, editorType);
                  _editor = (IEmApprovalSystemEditor)view;
                  ModuleView = view;
               }
               else {
                  Error = "The module's approval view is not installed in this application.";
               }
            }

            if (_editor is not null) await _editor.LoadAsync(new ApprovalSystemEditorContext(detail, Service, RefreshAsync));
         }

         NavigationEntry?.SetTitle($"Approval - {detail.Header.cApprovalName} · {detail.Header.cApprovalRequestId}");
      }

      private Task DecideAsync(bool approve) => RunAsync(async () => {
         if (Detail is null) return;
         var step = IsSystem ? SelectedStep?.Step : null;
         if (IsSystem && step is null) return;

         if (approve && Fields.Any(x => x.Conflict)) {
            Error = "Some values changed after this request was made. Resolve the conflict on the desktop client.";
            return;
         }

         var subject = step?.cApprovalRequestStepName ?? Detail.Header.cSummary;
         var note = await AskNoteAsync(approve, subject);
         if (note is null) return;

         if (step is not null) {
            if (_editor is null) {
               Error = "Install the module's approval view before signing.";
               return;
            }

            var check = approve ? await _editor.ValidateSignAsync(step) : await _editor.ValidateRejectAsync(step);
            if (!check.Ok) {
               Error = check.Message ?? "The module did not allow this decision.";
               return;
            }
         }

         var result = await Service.PostGetMeta_ApprovalDecide(new ApprovalDecision(Detail.Header.cApprovalRequestId, approve,
            step?.cApprovalRequestStepId, string.IsNullOrWhiteSpace(note) ? null : note.Trim()));
         Notice = result.Message ?? string.Empty;
         await LoadAsync();
         _ = EmApp?.RefreshHubTasksAsync();
      });

      // Returns null when cancelled. A rejection must say why; a signature note is optional.
      private async Task<string?> AskNoteAsync(bool approve, string subject) {
         if (DialogPage is not { } page) return null;
         while (true) {
            var note = await page.DisplayPromptAsync(approve ? (IsSystem ? "Sign" : "Approve") : "Reject", subject,
               approve ? (IsSystem ? "Sign" : "Approve") : "Reject", "Cancel",
               approve ? "Note (optional)" : "Reason (required)", maxLength: 400);
            if (note is null) return null;
            if (approve || !string.IsNullOrWhiteSpace(note)) return note;
            await page.DisplayAlertAsync("Reject", "Write the reason of the rejection.", "OK");
         }
      }

      private void RaiseState() {
         foreach (var property in new[] {
                     nameof(Caption), nameof(Summary), nameof(SubSummary), nameof(HasSubSummary), nameof(Status),
                     nameof(LevelText), nameof(RequestedText), nameof(HasSteps), nameof(HasFields), nameof(HasNoComments),
                     nameof(HasPreview), nameof(CanDecide), nameof(DecisionCaption), nameof(ApproveText)
                  })
            NotifyChanged(property);
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }
   }
}
