using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Dialogs;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
namespace Em.Ui.Wpf.Navigations;

/// <summary>Approval request list and an embeddable Data or System detail shell.</summary>
public partial class ApprovalManager : UserControl, INavigationBody {
   /// <summary>public ApprovalManager() approval UI member.</summary>
   public ApprovalManager() { InitializeComponent(); }
   /// <summary>public ApprovalManager(EmApp app) : this() approval UI member.</summary>
   public ApprovalManager(EmApp app) : this() { Vm.EmApp = app; }
   /// <summary>public ApprovalManagerVm Vm approval UI member.</summary>
   public ApprovalManagerVm Vm => (ApprovalManagerVm)DataContext;
   /// <summary>public async Task InitializeAsync(EmApp app, ApprovalManagerNavigationPayload payload) approval UI member.</summary>
   public async Task InitializeAsync(EmApp app, ApprovalManagerNavigationPayload payload) { Vm.EmApp = app; Vm.Configure(payload); await Vm.RefreshAsync(); }
   /// <summary>public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) approval UI member.</summary>
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) { Vm.Configure(args.Data); return Vm.RefreshAsync(); }
   /// <summary>public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) approval UI member.</summary>
   private void OnRequestDoubleClick(object sender, MouseButtonEventArgs args) {
      if (args.ChangedButton == MouseButton.Left && Vm.SelectedRequest?.cApprovalRequestKind == ApprovalKind.System)
         Vm.Commands[nameof(Vm.OpenSystemCommand)]?.Execute();
   }
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
   /// <summary>public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) approval UI member.</summary>
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshAsync();
   /// <summary>public Task OnRelease(INavigation sender) approval UI member.</summary>
   public Task OnRelease(INavigation sender) { Vm.Release(); return Task.CompletedTask; }
}
/// <summary>One proposed column and the approver's explicit override reason.</summary>
public sealed class ApprovalFieldVm(ta_ApprovalRequestItemField row) : MvvmModelBase {
   /// <summary>public ta_ApprovalRequestItemField Row approval UI member.</summary>
   public ta_ApprovalRequestItemField Row { get; } = row;
   /// <summary>public string Column approval UI member.</summary>
   public string Column => Row.cApprovalRequestItemFieldName;
   /// <summary>public string? Original approval UI member.</summary>
   public string? Original => Row.cApprovalRequestItemFieldOld;
   /// <summary>public string? Current approval UI member.</summary>
   public string? Current => Row.cApprovalRequestItemFieldCurrent;
   /// <summary>public string? Proposed approval UI member.</summary>
   public string? Proposed => Row.cApprovalRequestItemFieldNew;
   /// <summary>public bool Conflict approval UI member.</summary>
   public bool Conflict => Row.cApprovalRequestItemFieldConflict;
   /// <summary>public string Reason approval UI member.</summary>
   public string Reason { get => Get(string.Empty); set => Set(value); }
}
/// <summary>One snapshotted System step and its signer cards.</summary>
public sealed class ApprovalStepCardVm(ApprovalManagerVm owner, ta_ApprovalRequestStep step) : MvvmModelBase {
   /// <summary>public ta_ApprovalRequestStep Step approval UI member.</summary>
   public ta_ApprovalRequestStep Step { get; } = step;
   /// <summary>public string Heading approval UI member.</summary>
   public string Heading => $"L{Step.cApprovalRequestStepLevel} · {Step.cApprovalRequestStepName}";
   /// <summary>public string Status approval UI member.</summary>
   public string Status => IsSigned ? "Signed" : IsRejected ? "Rejected" : IsActive ? "Your turn" : "Waiting";
   public string LevelCaption => $"LEVEL {Step.cApprovalRequestStepLevel}";
   public bool IsSigned => Step.cApprovalRequestStepStage == ApprovalStepStatus.Approved;
   public bool IsRejected => Step.cApprovalRequestStepStage == ApprovalStepStatus.Rejected;
   public bool IsActive => owner.Detail?.Header.cApprovalRequestStage == ApprovalStage.Pending && Step.cApprovalRequestStepLevel == owner.Detail.Header.cApprovalRequestLevel && !IsSigned && !IsRejected;
   private ta_ApprovalRequestSignature? Signature => owner.Detail?.Signatures.FirstOrDefault(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId);
   private ta_ApprovalRequestStepCandidate? Primary => owner.Detail?.Candidates.FirstOrDefault(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId && !x.cApprovalRequestStepCandidateBackup);
   public string SignerName => Primary is null ? "No signer" : owner.UserName(Primary.cUserId);
   public string BackupNames => string.Join("\n", owner.Detail!.Candidates.Where(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId && x.cApprovalRequestStepCandidateBackup).Select(x => $"{owner.UserName(x.cUserId)} · four-eyes backup"));
   public string ViewerRole => owner.Detail?.Candidates.FirstOrDefault(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId && x.cUserId == owner.EmApp?.ActiveUser?.cUserId) is { } candidate
      ? candidate.cApprovalRequestStepCandidateBackup ? "You are a four-eyes backup" : "You are the signer" : string.Empty;
   public string SignatureText => Signature is { } signature ? $"{owner.UserName(signature.cUserId)} · {signature.datestamp:g}{(signature.cApprovalRequestSignatureAdmin ? " · ADMIN" : "")}{(signature.cApprovalRequestSignatureBackup ? " · FOUR-EYES BACKUP" : "")}" : string.Empty;
   public string VerificationCode => Signature?.cApprovalRequestSignatureCode ?? string.Empty;
   public string DecisionNote => Signature?.cApprovalRequestSignatureNote ?? string.Empty;
   public bool HasNote => !string.IsNullOrEmpty(DecisionNote);
   public bool IsMyTurn => owner.Detail?.DecidableStepIds.Contains(Step.cApprovalRequestStepId) == true;
   /// <summary>public string Candidates approval UI member.</summary>
   public string Candidates => string.Join("\n", owner.Detail!.Candidates.Where(x => x.cApprovalRequestStepId == step.cApprovalRequestStepId)
      .Select(x => $"{owner.UserName(x.cUserId)}{(x.cApprovalRequestStepCandidateBackup ? " · four-eyes backup" : " · signer")}{(x.cUserId == owner.EmApp?.ActiveUser?.cUserId ? " · you" : "")}"));
   /// <summary>public ApprovalManagerVm Owner approval UI member.</summary>
   public ApprovalManagerVm Owner => owner;
   /// <summary>public void RefreshState() approval UI member.</summary>
   public void RefreshState() => NotifyChanged(nameof(CanDecide));
   /// <summary>public bool CanDecide approval UI member.</summary>
   public bool CanDecide => !owner.IsBusy && owner.Detail?.DecidableStepIds.Contains(Step.cApprovalRequestStepId) == true;
}
/// <summary>Attachment display without mixing its identity with its caption.</summary>
public sealed record ApprovalFileVm(ta_ApprovalRequestAttachment Row) {
   public string Name => Row.cApprovalRequestAttachmentName;
   public string Meta => $"{Row.cApprovalRequestAttachmentSize:N0} bytes · {Row.datestamp:g}";
   public bool CanPreview => Path.GetExtension(Name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
}
/// <summary>A chronological comment or recorded decision in the viewer sidebar.</summary>
public sealed record ApprovalCommentVm(string Author, DateTime When, string Text, string Decision) {
   public string TimeText => When.ToString("g");
}
/// <summary>Shared approval state; commands consume only server-returned capabilities.</summary>
public sealed class ApprovalManagerVm : MvvmModelBase {
   private ApprovalManagerNavigationPayload _payload = new();
   private IEmApprovalSystemEditor? _editor;
   private bool _loadingRows;
   private int _version;
   private bool _released;
   private IApprovalServices Service => EmApp!.ServiceProvider.GetRequiredService<IApprovalServices>();
   /// <summary>public Task RefreshCommand() approval UI member.</summary>
   public Task RefreshCommand() => RefreshAsync();
   /// <summary>public Task ApproveCommand() approval UI member.</summary>
   public Task ApproveCommand() => DecideAsync(true, null);
   /// <summary>public Task RejectCommand() approval UI member.</summary>
   public Task RejectCommand() => DecideAsync(false, null);
   /// <summary>public Task SignStepCommand(ApprovalStepCardVm step) approval UI member.</summary>
   public Task SignStepCommand(ApprovalStepCardVm step) => DecideAsync(true, step.Step);
   /// <summary>public Task RejectStepCommand(ApprovalStepCardVm step) approval UI member.</summary>
   public Task RejectStepCommand(ApprovalStepCardVm step) => DecideAsync(false, step.Step);
   /// <summary>public Task WithdrawCommand() approval UI member.</summary>
   public Task WithdrawCommand() => WithdrawAsync();
   /// <summary>public Task VoidCommand() approval UI member.</summary>
   public Task VoidCommand() => VoidAsync();
   /// <summary>public Task ReinstateCommand() approval UI member.</summary>
   public Task ReinstateCommand() => ReinstateAsync();
   /// <summary>public Task CommentCommand() approval UI member.</summary>
   public Task CommentCommand() => CommentAsync();
   /// <summary>public Task AttachCommand() approval UI member.</summary>
   public Task AttachCommand() => AttachAsync();
   /// <summary>public Task DownloadCommand(ta_ApprovalRequestAttachment attachment) approval UI member.</summary>
   public Task DownloadCommand(ta_ApprovalRequestAttachment attachment) => DownloadAsync(attachment);
   /// <summary>public Task PreviewCommand() approval UI member.</summary>
   public Task PreviewCommand() => PreviewAsync();
   /// <summary>public Task SourceCommand() approval UI member.</summary>
   public Task SourceCommand() => _editor!.OpenSourceAsync();
   /// <summary>public Task NextCommand() approval UI member.</summary>
   public Task NextCommand() => NextAsync();
   /// <summary>public Task PreviousCommand() approval UI member.</summary>
   public Task PreviousCommand() => PreviousAsync();
   public Task OpenSystemCommand() => EmApp!.NavigateTo("em.approval.editor", new ApprovalManagerNavigationPayload {
      Kind = ApprovalKind.System, Name = SelectedRequest!.cApprovalName, ApprovalRequestId = SelectedRequest.cApprovalRequestId,
      WaitingForMeOnly = false, SystemViewer = true
   });
   public void SignatureNoteCommand(ApprovalStepCardVm step) => DialogOwner?.ShowMboxInfo(step.DecisionNote, step.Step.cApprovalRequestStepName);
   public Task PreviewAttachmentCommand(ta_ApprovalRequestAttachment row) => EmApp!.NavigateTo(Em.Ui.Wpf.Core.EmApp.PdfViewerNavigationName,
      new PdfViewerNavigationPayload(row.cApprovalRequestAttachmentName, _ => Service.GetMeta_ApprovalAttachment(row.cApprovalRequestAttachmentId)));

   /// <summary>public ApprovalManagerVm() approval UI member.</summary>
   public ApprovalManagerVm() {
      RegisterCommand(nameof(OpenSystemCommand), OpenSystemCommand, () => !IsBusy && SelectedRequest?.cApprovalRequestKind == ApprovalKind.System);
      RegisterCommand<ApprovalStepCardVm>(nameof(SignatureNoteCommand), SignatureNoteCommand, step => !IsBusy && step?.HasNote == true);
      RegisterCommand<ta_ApprovalRequestAttachment>(nameof(PreviewAttachmentCommand), PreviewAttachmentCommand, row => !IsBusy && row is not null && Path.GetExtension(row.cApprovalRequestAttachmentName).Equals(".pdf", StringComparison.OrdinalIgnoreCase));
      RegisterCommand(nameof(RefreshCommand), RefreshAsync, () => !IsBusy);
      RegisterCommand(nameof(ApproveCommand), () => DecideAsync(true, null), () => !IsBusy && IsData && Detail?.CanDecide == true);
      RegisterCommand(nameof(RejectCommand), () => DecideAsync(false, null), () => !IsBusy && IsData && Detail?.CanDecide == true);
      RegisterCommand<ApprovalStepCardVm>(nameof(SignStepCommand), x => DecideAsync(true, x.Step), x => x?.CanDecide == true);
      RegisterCommand<ApprovalStepCardVm>(nameof(RejectStepCommand), x => DecideAsync(false, x.Step), x => x?.CanDecide == true);
      RegisterCommand(nameof(WithdrawCommand), WithdrawAsync, () => !IsBusy && Detail?.CanWithdraw == true);
      RegisterCommand(nameof(VoidCommand), VoidAsync, () => !IsBusy && Detail?.CanVoid == true);
      RegisterCommand(nameof(ReinstateCommand), ReinstateAsync, () => !IsBusy && Detail?.CanReinstate == true);
      RegisterCommand(nameof(CommentCommand), CommentAsync, () => !IsBusy && Detail is not null && !string.IsNullOrWhiteSpace(Comment));
      RegisterCommand(nameof(AttachCommand), AttachAsync, () => !IsBusy && Detail?.CanAttach == true);
      RegisterCommand<ta_ApprovalRequestAttachment>(nameof(DownloadCommand), DownloadAsync, x => !IsBusy && x is not null);
      RegisterCommand(nameof(PreviewCommand), PreviewAsync, () => !IsBusy && _editor?.HasPreview == true);
      RegisterCommand(nameof(SourceCommand), () => _editor!.OpenSourceAsync(), () => !IsBusy && _editor?.HasSource == true);
      RegisterCommand(nameof(NextCommand), NextAsync, () => !IsBusy && Page*50 < Total);
      RegisterCommand(nameof(PreviousCommand), PreviousAsync, () => !IsBusy && Page > 1);
   }
   /// <summary>public static bool CanOpenManager(EmApp app) approval UI member.</summary>
   public static bool CanOpenManager(EmApp app) => app.ServiceProvider.GetRequiredService<ApprovalAccessCatalog>().CanOpen;
   /// <summary>public ObservableCollection&lt;ApprovalCapability&gt; Definitions approval UI member.</summary>
   public ObservableCollection<ApprovalCapability> Definitions { get; } = [];
   /// <summary>public ObservableCollection&lt;ta_ApprovalRequest&gt; Rows approval UI member.</summary>
   public ObservableCollection<ta_ApprovalRequest> Rows { get; } = [];
   /// <summary>public ObservableCollection&lt;ApprovalFieldVm&gt; Fields approval UI member.</summary>
   public ObservableCollection<ApprovalFieldVm> Fields { get; } = [];
   /// <summary>public ObservableCollection&lt;ApprovalStepCardVm&gt; Steps approval UI member.</summary>
   public ObservableCollection<ApprovalStepCardVm> Steps { get; } = [];
   /// <summary>public ObservableCollection&lt;string&gt; Timeline approval UI member.</summary>
   public ObservableCollection<string> Timeline { get; } = [];
   /// <summary>public ObservableCollection&lt;ta_ApprovalRequestAttachment&gt; Attachments approval UI member.</summary>
   public ObservableCollection<ta_ApprovalRequestAttachment> Attachments { get; } = [];
   /// <summary>public ObservableCollection&lt;ApprovalToolbarItem&gt; ModuleToolbar approval UI member.</summary>
   public ObservableCollection<ApprovalFileVm> Files { get; } = [];
   public ObservableCollection<ApprovalCommentVm> Comments { get; } = [];
   public ObservableCollection<ApprovalToolbarItem> ModuleToolbar { get; } = [];
   /// <summary>public ApprovalCapability? SelectedDefinition approval UI member.</summary>
   public ApprovalCapability? SelectedDefinition { get => Get<ApprovalCapability?>(); set { if (Set(value) && !_loadingRows) { Page = 1; _ = RefreshAsync(); } } }
   /// <summary>public ta_ApprovalRequest? SelectedRequest approval UI member.</summary>
   public ta_ApprovalRequest? SelectedRequest { get => Get<ta_ApprovalRequest?>(); set { if (Set(value) && !_loadingRows) _ = RunAsync(LoadDetailAsync); } }
   /// <summary>public ApprovalRequestDetail? Detail approval UI member.</summary>
   public ApprovalRequestDetail? Detail { get => Get<ApprovalRequestDetail?>(); private set => Set(value); }
   /// <summary>public object? ModuleView approval UI member.</summary>
   public object? ModuleView { get => Get<object?>(); private set => Set(value); }
   /// <summary>public string Error approval UI member.</summary>
   public string Error { get => Get(string.Empty); private set => Set(value); }
   /// <summary>public bool IsBusy approval UI member.</summary>
   public bool IsBusy { get => Get(false); private set { Set(value); RaiseState(); } }
   /// <summary>public bool IsNotBusy approval UI member.</summary>
   public bool IsNotBusy => !IsBusy;
   /// <summary>public bool IsCompact approval UI member.</summary>
   public bool IsCompact => _payload.Compact;
   /// <summary>public bool ShowFilters approval UI member.</summary>
   public bool ShowFilters => !IsCompact && !_payload.SystemViewer;
   public bool ShowManagerToolbar => !_payload.SystemViewer;
   public bool IsSignersOpen { get => Get(true); set { Set(value); NotifyChanged(nameof(SignersToggleText)); } }
   public bool IsSidePanelOpen { get => Get(true); set { Set(value); RaiseState(); } }
   public GridLength SideWidth => new(IsSidePanelOpen ? 340 : 0);
   public GridLength SideGap => new(IsSidePanelOpen ? 14 : 0);
   public string SignersToggleText => IsSignersOpen ? "Signers" : $"Signers · {Steps.Count(x => x.IsSigned)}/{Steps.Count}{(Steps.Any(x => x.IsMyTurn) ? " · your turn" : "")}";
   public string SidePanelToggleText => IsSidePanelOpen ? "Comments" : $"Comments · {Comments.Count}";
   public string RefreshedText { get => Get(string.Empty); private set => Set(value); }
   public string SourceCaption => _editor?.SourceCaption ?? "Open source";
   public string RequestStatus => Detail?.Header.cApprovalRequestStage == ApprovalStage.Approved ? "Completed" : Detail?.Header.cApprovalRequestStage.ToString() ?? string.Empty;
   /// <summary>public GridLength ListWidth approval UI member.</summary>
   public GridLength ListWidth => new(ShowFilters ? 340 : 0);
   /// <summary>public bool IsData approval UI member.</summary>
   public bool IsData => Detail?.Header.cApprovalRequestKind == ApprovalKind.Data;
   /// <summary>public bool IsSystem approval UI member.</summary>
   public bool IsSystem => Detail?.Header.cApprovalRequestKind == ApprovalKind.System;
   /// <summary>public bool HasPreview approval UI member.</summary>
   public bool HasPreview => _editor?.HasPreview == true;
   /// <summary>public bool HasSource approval UI member.</summary>
   public bool HasSource => _editor?.HasSource == true;
   /// <summary>public string Title approval UI member.</summary>
   public string Title => Detail is null ? "Select an approval request" : $"{Detail.Header.cApprovalName} · {Detail.Header.cApprovalRequestStage}";
   /// <summary>public string Summary approval UI member.</summary>
   public string Summary => Detail?.Header.cSummary ?? string.Empty;
   /// <summary>public string CreateSnapshots approval UI member.</summary>
   public string CreateSnapshots { get => Get(string.Empty); private set => Set(value); }
   /// <summary>public string Note approval UI member.</summary>
   public string Note { get => Get(string.Empty); set => Set(value); }
   /// <summary>public string Comment approval UI member.</summary>
   public string Comment { get => Get(string.Empty); set { Set(value); RaiseState(); } }
   /// <summary>public string Search approval UI member.</summary>
   public string Search { get => Get(string.Empty); set => Set(value); }
   /// <summary>public bool WaitingForMe approval UI member.</summary>
   public bool WaitingForMe { get => Get(true); set => Set(value); }
   /// <summary>public ApprovalStage? Stage approval UI member.</summary>
   public ApprovalStage? Stage { get => Get<ApprovalStage?>(); set => Set(value); }
   /// <summary>public ApprovalStage?[] Stages approval UI member.</summary>
   public ApprovalStage?[] Stages { get; } = [null, .. Enum.GetValues<ApprovalStage>()];
   /// <summary>public int Page approval UI member.</summary>
   public int Page { get => Get(1); private set => Set(value); }
   /// <summary>public int Total approval UI member.</summary>
   public int Total { get => Get(0); private set => Set(value); }
   /// <summary>public string PageLabel approval UI member.</summary>
   public string PageLabel => $"Page {Page} · {Total} requests";
   /// <summary>public void Configure(object? input) approval UI member.</summary>
   public void Configure(object? input) {
      _released = false;
      _payload = input as ApprovalManagerNavigationPayload ?? new();
      if (input is string text && text.IndexOf(':') is var split && split > 0 && Enum.TryParse<ApprovalKind>(text[..split], out var kind)) {
         _payload.Kind = kind; _payload.Name = text[(split+1)..];
      }
      WaitingForMe = _payload.WaitingForMeOnly; Page = 1; RaiseState();
   }
   /// <summary>public void Release() approval UI member.</summary>
   public void Release() { _released = true; _version++; ModuleView = null; _editor = null; }
   private void RaiseState() {
      foreach (var property in new[] { nameof(IsNotBusy), nameof(IsCompact), nameof(ShowFilters), nameof(ListWidth), nameof(IsData), nameof(IsSystem), nameof(Title), nameof(Summary), nameof(PageLabel), nameof(HasPreview), nameof(HasSource), nameof(ShowManagerToolbar), nameof(SideWidth), nameof(SideGap), nameof(SignersToggleText), nameof(SidePanelToggleText), nameof(SourceCaption), nameof(RequestStatus) }) NotifyChanged(property);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
      foreach (var step in Steps) step.RefreshState();
   }
   private async Task RunAsync(Func<Task> work) {
      if (IsBusy || _released) return;
      IsBusy = true; Error = string.Empty;
      try { await work(); } catch (Exception exception) { Error = exception.Message; }
      finally { IsBusy = false; }
   }
   /// <summary>public Task RefreshAsync() approval UI member.</summary>
   public Task RefreshAsync() => RunAsync(async () => {
      var version = ++_version;
      var definitions = await Service.GetMeta_ApprovalCapabilities();
      if (_released || version != _version) return;
      _loadingRows = true;
      try {
         var selected = SelectedDefinition;
         Definitions.Clear(); foreach (var definition in definitions.Where(x => x.CanView)) Definitions.Add(definition);
         SelectedDefinition = Definitions.FirstOrDefault(x => x.Kind == (selected?.Kind ?? _payload.Kind) && x.Name == (selected?.Name ?? _payload.Name));
         var oldId = SelectedRequest?.cApprovalRequestId;
         var result = await Service.PostGetMeta_ApprovalRequests(new ApprovalQuery(SelectedDefinition?.Kind ?? _payload.Kind,
            SelectedDefinition?.Name ?? _payload.Name, _payload.SubjectKey, Stage, WaitingForMe, Search, Page, 50));
         if (_released || version != _version) return;
         Rows.Clear(); foreach (var row in result.Items) Rows.Add(row); Total = result.Total;
         SelectedRequest = Rows.FirstOrDefault(x => x.cApprovalRequestId == (_payload.ApprovalRequestId ?? oldId)) ?? Rows.FirstOrDefault();
         if (_payload.ApprovalRequestId is { } direct && SelectedRequest?.cApprovalRequestId != direct) {
            var detail = await Service.GetMeta_ApprovalRequest(direct); Rows.Insert(0, detail.Header); SelectedRequest = detail.Header;
         }
         _payload.ApprovalRequestId = null;
         await LoadDetailAsync();
      } finally { _loadingRows = false; RaiseState(); }
   });
   private async Task LoadDetailAsync() {
      var id = SelectedRequest?.cApprovalRequestId; var version = ++_version;
      Detail = null; ModuleView = null; _editor = null; Fields.Clear(); Steps.Clear(); Timeline.Clear(); Attachments.Clear(); Files.Clear(); Comments.Clear(); ModuleToolbar.Clear(); CreateSnapshots = string.Empty;
      if (id is null) { RaiseState(); return; }
      var detail = await Service.GetMeta_ApprovalRequest(id);
      if (_released || version != _version || SelectedRequest?.cApprovalRequestId != id) return;
      Detail = detail;
      foreach (var field in detail.Fields) Fields.Add(new ApprovalFieldVm(field));
      CreateSnapshots = string.Join("\n\n", detail.Items.Where(x => x.cApprovalRequestItemOperation != ApprovalItemOperation.Update).Select(x => $"{x.cApprovalRequestItemOperation} · {x.cSubjectKey ?? "Key assigned on approval"}\n{x.json_object}"));
      foreach (var signature in detail.Signatures) Timeline.Add($"{signature.datestamp:g} · {UserName(signature.cUserId)} · {(signature.cApprovalRequestSignatureApproved ? "Signed" : "Rejected")} · {signature.cApprovalRequestSignatureCode}{(signature.cApprovalRequestSignatureAdmin ? " · ADMIN" : "")}{(signature.cApprovalRequestSignatureBackup ? " · FOUR-EYES BACKUP" : "")}\n{signature.cApprovalRequestSignatureNote}");
      foreach (var comment in detail.Comments) Timeline.Add($"{comment.datestamp:g} · {UserName(comment.cUserId)}\n{comment.cApprovalRequestCommentText}");
      foreach (var attachment in detail.Attachments) { Attachments.Add(attachment); Files.Add(new ApprovalFileVm(attachment)); }
      var comments = detail.Comments.Select(x => new ApprovalCommentVm(UserName(x.cUserId), x.datestamp, x.cApprovalRequestCommentText, string.Empty))
         .Concat(detail.Signatures.Select(x => new ApprovalCommentVm(UserName(x.cUserId), x.datestamp, x.cApprovalRequestSignatureNote ?? string.Empty,
            (x.cApprovalRequestSignatureApproved ? "Signed" : "Rejected") + (x.cApprovalRequestSignatureBackup ? " · four-eyes backup" : x.cApprovalRequestSignatureAdmin ? " · administrator" : string.Empty))));
      foreach (var comment in comments.OrderBy(x => x.When)) Comments.Add(comment);
      RefreshedText = $"Refreshed {DateTime.Now:HH:mm:ss}";
      if (detail.Header.cApprovalRequestKind == ApprovalKind.System) {
         var editorType = EmApp!.ServiceProvider.GetRequiredService<ApprovalEditorRegistry>().Find(detail.Header.cApprovalName);
         if (editorType is not null) {
            var view = ActivatorUtilities.CreateInstance(EmApp.ServiceProvider, editorType);
            _editor = (IEmApprovalSystemEditor)view; ModuleView = view;
            await _editor.LoadAsync(new ApprovalSystemEditorContext(detail, Service, RefreshAsync));
            foreach (var item in _editor.ToolbarItems) ModuleToolbar.Add(item);
         } else Error = "The module's System approval editor is not installed.";
         foreach (var step in detail.Steps) Steps.Add(new ApprovalStepCardVm(this, step));
      }
      RaiseState();
   }
   /// <summary>public string UserName(string id) approval UI member.</summary>
   public string UserName(string id) => Detail?.Users.FirstOrDefault(x => x.UserId == id)?.Name ?? id;
   private Task DecideAsync(bool approve, ta_ApprovalRequestStep? step) => RunAsync(async () => {
      if (Detail is null) return;
      if (step is not null) {
         var prompt = new ApprovalDecisionDialog(approve ? "Sign" : "Reject", step.cApprovalRequestStepName, !approve) { Owner = DialogOwner };
         if (prompt.ShowDialog() != true) return;
         Note = prompt.Vm.Note;
         if (_editor is null) { Error = "Install the module's approval editor before signing."; return; }
         var check = approve ? await _editor.ValidateSignAsync(step) : await _editor.ValidateRejectAsync(step);
         if (!check.Ok) { Error = check.Message ?? "The module did not allow this decision."; return; }
      }
      var result = await Service.PostGetMeta_ApprovalDecide(new ApprovalDecision(Detail.Header.cApprovalRequestId, approve,
         step?.cApprovalRequestStepId, Note, Fields.Where(x => x.Conflict && !string.IsNullOrWhiteSpace(x.Reason)).Select(x => new ApprovalFieldOverride(x.Row.cApprovalRequestItemId, x.Column, x.Reason)).ToArray()));
      Error = result.Message ?? string.Empty; await LoadDetailAsync();
   });
   private Task WithdrawAsync() => RunAsync(async () => { await Service.PostMeta_ApprovalWithdraw(Detail!.Header.cApprovalRequestId, Note); await LoadDetailAsync(); });
   private Task VoidAsync() => RunAsync(async () => {
      var prompt = new ApprovalDecisionDialog("Void", "The request becomes void and keeps its signatures as history. A new approval must be requested afterward.", true) { Owner = DialogOwner };
      if (prompt.ShowDialog() != true) return;
      await Service.PostMeta_ApprovalSystemVoid(Detail!.Header.cApprovalRequestId, prompt.Vm.Note); await LoadDetailAsync();
   });
   private Task ReinstateAsync() => RunAsync(async () => {
      var prompt = new ApprovalDecisionDialog("Reinstate", "Creates a new request from current settings. Every step is signed again; the current signatures remain as history.", true) { Owner = DialogOwner };
      if (prompt.ShowDialog() != true) return;
      var old = Detail!.Header; var id = await Service.PostGetMeta_ApprovalSystemReinstate(old.cApprovalRequestId, old.cSummary, prompt.Vm.Note);
      if (_editor is not null) await _editor.OnReinstatedAsync(old.cApprovalRequestId, id, old.cSubjectKey!);
      _loadingRows = true; SelectedRequest = (await Service.GetMeta_ApprovalRequest(id)).Header; _loadingRows = false;
      await LoadDetailAsync();
   });
   private Task CommentAsync() => RunAsync(async () => { await Service.PostMeta_ApprovalComment(Detail!.Header.cApprovalRequestId, Comment); Comment = string.Empty; await LoadDetailAsync(); });
   private Task AttachAsync() => RunAsync(async () => {
      var dialog = new OpenFileDialog(); if (dialog.ShowDialog(DialogOwner) != true) return;
      await using var stream = File.OpenRead(dialog.FileName);
      await Service.PostGetMeta_ApprovalAttachment(new ApprovalAttachmentUpload(Detail!.Header.cApprovalRequestId, null, Path.GetFileName(dialog.FileName)), stream);
      await LoadDetailAsync();
   });
   private Task DownloadAsync(ta_ApprovalRequestAttachment attachment) => RunAsync(async () => {
      var dialog = new SaveFileDialog { FileName = attachment.cApprovalRequestAttachmentName };
      if (dialog.ShowDialog(DialogOwner) != true) return;
      await using var stream = await Service.GetMeta_ApprovalAttachment(attachment.cApprovalRequestAttachmentId);
      await using var file = File.Create(dialog.FileName); await stream.CopyToAsync(file);
   });
   private async Task PreviewAsync() {
      var editor = _editor!;
      await EmApp!.NavigateTo(Em.Ui.Wpf.Core.EmApp.PdfViewerNavigationName, new PdfViewerNavigationPayload(Title,
         async ct => await editor.GetPreviewAsync(ct) ?? throw new InvalidOperationException("The module returned no preview.")));
   }
   private async Task NextAsync() { Page++; await RefreshAsync(); }
   private async Task PreviousAsync() { Page--; await RefreshAsync(); }
}
