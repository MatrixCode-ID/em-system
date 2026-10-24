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
   /// <summary>Creates an approval request view.</summary>
   public ApprovalManager() { InitializeComponent(); Vm.Selection = RequestList.SelectedItems; }
   // SelectedItems is not a bindable WPF dependency property; bridge only command invalidation.
   private void SelectionChanged(object sender, SelectionChangedEventArgs args) => Vm.RefreshSelection();
   /// <summary>Creates an approval request view for the supplied host application.</summary>
   public ApprovalManager(EmApp app) : this() { Vm.EmApp = app; }
   /// <summary>Bindable state of the approval view.</summary>
   public ApprovalManagerVm Vm => (ApprovalManagerVm)DataContext;
   /// <summary>Initializes the embedded view with a filter and loads server-authorized requests.</summary>
   public async Task InitializeAsync(EmApp app, ApprovalManagerNavigationPayload payload) { Vm.EmApp = app; Vm.Configure(payload); await Vm.RefreshAsync(); }
   /// <summary>Handles the navigation lifecycle event.</summary>
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) { Vm.Configure(args.Data); return Vm.RefreshAsync(); }
   /// <summary>Handles the navigation lifecycle event.</summary>
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
   /// <summary>Handles the navigation lifecycle event.</summary>
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshAsync();
   /// <summary>Handles the navigation lifecycle event.</summary>
   public Task OnRelease(INavigation sender) { Vm.Release(); return Task.CompletedTask; }
}
/// <summary>One proposed column and the approver's explicit override reason.</summary>
public sealed class ApprovalFieldVm(ta_ApprovalRequestItemField row) : MvvmModelBase {
   /// <summary>Bindable row for the approval view.</summary>
   public ta_ApprovalRequestItemField Row { get; } = row;
   /// <summary>Bindable column for the approval view.</summary>
   public string Column => Row.cApprovalRequestItemFieldName;
   /// <summary>Bindable original for the approval view.</summary>
   public string? Original => Row.cApprovalRequestItemFieldOld;
   /// <summary>Bindable current for the approval view.</summary>
   public string? Current => Row.cApprovalRequestItemFieldCurrent;
   /// <summary>Bindable proposed for the approval view.</summary>
   public string? Proposed => Row.cApprovalRequestItemFieldNew;
   /// <summary>Bindable conflict for the approval view.</summary>
   public bool Conflict => Row.cApprovalRequestItemFieldConflict;
   /// <summary>Bindable reason for the approval view.</summary>
   public string Reason { get => Get(Row.cApprovalRequestItemFieldOverrideReason ?? string.Empty); set => Set(value); }
}
/// <summary>One snapshotted System step and its signer cards.</summary>
public sealed class ApprovalStepCardVm(ApprovalManagerVm owner, ta_ApprovalRequestStep step) : MvvmModelBase {
   /// <summary>Bindable step for the approval view.</summary>
   public ta_ApprovalRequestStep Step { get; } = step;
   /// <summary>Bindable heading for the approval view.</summary>
   public string Heading => $"L{Step.cApprovalRequestStepLevel} · {Step.cApprovalRequestStepName}";
   /// <summary>Bindable status for the approval view.</summary>
   public string Status => IsSigned ? "Signed" : IsRejected ? "Rejected" : IsMyTurn ? "Your turn" : "Waiting";
   /// <summary>Level number displayed above the step name.</summary>
   public string LevelCaption => $"LEVEL {Step.cApprovalRequestStepLevel}";
   /// <summary>Whether an approval signature has completed this step.</summary>
   public bool IsSigned => Step.cApprovalRequestStepStage == ApprovalStepStatus.Approved;
   /// <summary>Whether this step rejected the request.</summary>
   public bool IsRejected => Step.cApprovalRequestStepStage == ApprovalStepStatus.Rejected;
   /// <summary>Whether the request is currently waiting at this step level.</summary>
   public bool IsActive => owner.Detail?.Header.cApprovalRequestStage == ApprovalStage.Pending && Step.cApprovalRequestStepLevel == owner.Detail.Header.cApprovalRequestLevel && !IsSigned && !IsRejected;
   private ta_ApprovalRequestSignature? Signature => owner.Detail?.Signatures.FirstOrDefault(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId);
   private ta_ApprovalRequestStepCandidate? Primary => owner.Detail?.Candidates.FirstOrDefault(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId && !x.cApprovalRequestStepCandidateBackup);
   /// <summary>Primary signer display name.</summary>
   public string SignerName => Primary is null ? "No signer" : owner.UserName(Primary.cUserId);
   /// <summary>Four-eyes backup names and their role.</summary>
   public string BackupNames => string.Join("\n", owner.Detail!.Candidates.Where(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId && x.cApprovalRequestStepCandidateBackup).Select(x => $"{owner.UserName(x.cUserId)} · four-eyes backup"));
   /// <summary>Role of the signed-in user in this step.</summary>
   public string ViewerRole => owner.Detail?.Candidates.FirstOrDefault(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId && x.cUserId == owner.EmApp?.ActiveUser?.cUserId) is { } candidate
      ? candidate.cApprovalRequestStepCandidateBackup ? "You are a four-eyes backup" : "You are the signer" : string.Empty;
   /// <summary>Signer, decision time and administrative or backup markers.</summary>
   public string SignatureText => Signature is { } signature ? $"{owner.UserName(signature.cUserId)} · {signature.datestamp:g}{(signature.cApprovalRequestSignatureAdmin ? " · ADMIN" : "")}{(signature.cApprovalRequestSignatureBackup ? " · FOUR-EYES BACKUP" : "")}" : string.Empty;
   /// <summary>Public verification code recorded with the signature.</summary>
   public string VerificationCode => Signature?.cApprovalRequestSignatureCode ?? string.Empty;
   /// <summary>Note recorded with the step decision.</summary>
   public string DecisionNote => Signature?.cApprovalRequestSignatureNote ?? string.Empty;
   /// <summary>Whether a decision note is available.</summary>
   public bool HasNote => !string.IsNullOrEmpty(DecisionNote);
   /// <summary>Whether the server permits this caller to decide the step.</summary>
   public bool IsMyTurn => owner.Detail?.DecidableStepIds.Contains(Step.cApprovalRequestStepId) == true;
   /// <summary>Primary signer and ordered four-eyes backups.</summary>
   public string Candidates => string.Join("\n", owner.Detail!.Candidates.Where(x => x.cApprovalRequestStepId == Step.cApprovalRequestStepId)
      .Select(x => $"{owner.UserName(x.cUserId)}{(x.cApprovalRequestStepCandidateBackup ? " · four-eyes backup" : " · signer")}{(x.cUserId == owner.EmApp?.ActiveUser?.cUserId ? " · you" : "")}"));
   /// <summary>Bindable owner for the approval view.</summary>
   public ApprovalManagerVm Owner => owner;
   /// <summary>Bindable refresh state for the approval view.</summary>
   public void RefreshState() => NotifyChanged(nameof(CanDecide));
   /// <summary>Whether the caller can act on this step now.</summary>
   public bool CanDecide => !owner.IsBusy && owner.Detail?.DecidableStepIds.Contains(Step.cApprovalRequestStepId) == true;
}
/// <summary>Attachment display without mixing its identity with its caption.</summary>
public sealed record ApprovalFileVm(ta_ApprovalRequestAttachment Row) {
   /// <summary>Original attachment file name.</summary>
   public string Name => Row.cApprovalRequestAttachmentName;
   /// <summary>Attachment size and upload time.</summary>
   public string Meta => $"{Row.cApprovalRequestAttachmentSize:N0} bytes · {Row.datestamp:g}";
   /// <summary>Whether the file type supports the PDF preview.</summary>
   public bool CanPreview => Path.GetExtension(Name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
}
/// <summary>A chronological comment or recorded decision in the viewer sidebar.</summary>
public sealed record ApprovalCommentVm(string Author, DateTime When, string Text, string Decision) {
   /// <summary>Formatted timestamp of a comment or decision.</summary>
   public string TimeText => When.ToString("g");
}
/// <summary>A selectable approval definition, or all definitions when kind and name are absent.</summary>
public sealed record ApprovalDefinitionFilter(
   ApprovalKind? Kind, string? Name, string Title) {
   /// <summary>Readable selection text, including the kind for a specific approval.</summary>
   public string Caption => Kind is { } kind ? $"{kind} {Title}" : Title;
}
/// <summary>Shared approval state; commands consume only server-returned capabilities.</summary>
public sealed partial class ApprovalManagerVm : MvvmModelBase {
   private ApprovalManagerNavigationPayload _payload = new();
   private IEmApprovalSystemEditor? _editor;
   private bool _loadingRows;
   /// <summary>Live selection supplied by the request table.</summary>
   public System.Collections.IList Selection { get; set; } = Array.Empty<object>();
   private int _version;
   private bool _released;
   private IApprovalServices Service => EmApp!.ServiceProvider.GetRequiredService<IApprovalServices>();
   /// <summary>Runs the refresh action using the current authorized state.</summary>
   public Task RefreshCommand() => RefreshAsync();
   /// <summary>Runs the approve action using the current authorized state.</summary>
   public Task ApproveCommand() => DecideAsync(true, null);
   /// <summary>Runs the reject action using the current authorized state.</summary>
   public Task RejectCommand() => DecideAsync(false, null);
   /// <summary>Runs the sign step action using the current authorized state.</summary>
   public Task SignStepCommand(ApprovalStepCardVm step) => DecideAsync(true, step.Step);
   /// <summary>Runs the reject step action using the current authorized state.</summary>
   public Task RejectStepCommand(ApprovalStepCardVm step) => DecideAsync(false, step.Step);
   /// <summary>Runs the withdraw action using the current authorized state.</summary>
   public Task WithdrawCommand() => WithdrawAsync();
   /// <summary>Runs the void action using the current authorized state.</summary>
   public Task VoidCommand() => VoidAsync();
   /// <summary>Runs the reinstate action using the current authorized state.</summary>
   public Task ReinstateCommand() => ReinstateAsync();
   /// <summary>Runs the comment action using the current authorized state.</summary>
   public Task CommentCommand() => CommentAsync();
   /// <summary>Runs the attach action using the current authorized state.</summary>
   public Task AttachCommand() => AttachAsync();
   /// <summary>Runs the download action using the current authorized state.</summary>
   public Task DownloadCommand(ta_ApprovalRequestAttachment attachment) => DownloadAsync(attachment);
   /// <summary>Runs the preview action using the current authorized state.</summary>
   public Task PreviewCommand() => PreviewAsync();
   /// <summary>Runs the source action using the current authorized state.</summary>
   public Task SourceCommand() => _editor!.OpenSourceAsync();
   /// <summary>Runs the next action using the current authorized state.</summary>
   public Task NextCommand() => NextAsync();
   /// <summary>Runs the previous action using the current authorized state.</summary>
   public Task PreviousCommand() => PreviousAsync();
   /// <summary>Opens the supplied request in a separate editor tab.</summary>
   public Task OpenRequestCommand(ta_ApprovalRequest? request) {
      if (!OpenRequestCommandAllowed(request) || request is null) return Task.CompletedTask;
      return NavigateToRequestAsync(request);
   }
   private Task NavigateToRequestAsync(ta_ApprovalRequest request) {
      var payload = new ApprovalManagerNavigationPayload {
         Kind = request.cApprovalRequestKind, Name = request.cApprovalName, ApprovalRequestId = request.cApprovalRequestId,
         WaitingForMeOnly = false, SystemViewer = true
      };
      return NavigationEntry?.NavigateTo("em.approval.editor", payload)
         ?? EmApp!.NavigateTo("em.approval.editor", payload);
   }
   /// <summary>Shows the note recorded with a signature.</summary>
   public void SignatureNoteCommand(ApprovalStepCardVm step) => DialogOwner?.ShowMboxInfo(step.DecisionNote, step.Step.cApprovalRequestStepName);
   /// <summary>Opens a permitted PDF attachment in the shared viewer.</summary>
   public Task PreviewAttachmentCommand(ta_ApprovalRequestAttachment row) => EmApp!.NavigateTo(Em.Ui.Wpf.Core.EmApp.PdfViewerNavigationName,
      new PdfViewerNavigationPayload(row.cApprovalRequestAttachmentName, _ => Service.GetMeta_ApprovalAttachment(row.cApprovalRequestAttachmentId)));

   /// <summary>Creates the request commands.</summary>
   public ApprovalManagerVm() {
      RegisterBatchCommands();
      RegisterCommand<ta_ApprovalRequest?>(nameof(OpenRequestCommand), OpenRequestCommand, OpenRequestCommandAllowed);
      RegisterCommand<ApprovalStepCardVm>(nameof(SignatureNoteCommand), SignatureNoteCommand, SignatureNoteCommandAllowed);
      RegisterCommand<ta_ApprovalRequestAttachment>(nameof(PreviewAttachmentCommand), PreviewAttachmentCommand, PreviewAttachmentCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(ApproveCommand), ApproveCommand, ApproveCommandAllowed);
      RegisterCommand(nameof(RejectCommand), RejectCommand, RejectCommandAllowed);
      RegisterCommand<ApprovalStepCardVm>(nameof(SignStepCommand), SignStepCommand, SignStepCommandAllowed);
      RegisterCommand<ApprovalStepCardVm>(nameof(RejectStepCommand), RejectStepCommand, RejectStepCommandAllowed);
      RegisterCommand(nameof(WithdrawCommand), WithdrawCommand, WithdrawCommandAllowed);
      RegisterCommand(nameof(VoidCommand), VoidCommand, VoidCommandAllowed);
      RegisterCommand(nameof(ReinstateCommand), ReinstateCommand, ReinstateCommandAllowed);
      RegisterCommand(nameof(CommentCommand), CommentCommand, CommentCommandAllowed);
      RegisterCommand(nameof(AttachCommand), AttachCommand, AttachCommandAllowed);
      RegisterCommand<ta_ApprovalRequestAttachment>(nameof(DownloadCommand), DownloadCommand, DownloadCommandAllowed);
      RegisterCommand(nameof(PreviewCommand), PreviewCommand, PreviewCommandAllowed);
      RegisterCommand(nameof(SourceCommand), SourceCommand, SourceCommandAllowed);
      RegisterCommand(nameof(NextCommand), NextCommand, NextCommandAllowed);
      RegisterCommand(nameof(PreviousCommand), PreviousCommand, PreviousCommandAllowed);
   }
   /// <summary>Whether the current state permits this action.</summary>
   public bool OpenRequestCommandAllowed(ta_ApprovalRequest? request) => !IsBusy && request is not null;
   /// <summary>Whether the current state permits this action.</summary>
   public bool SignatureNoteCommandAllowed(ApprovalStepCardVm step) => !IsBusy && step?.HasNote == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool PreviewAttachmentCommandAllowed(ta_ApprovalRequestAttachment row) => !IsBusy && row is not null && Path.GetExtension(row.cApprovalRequestAttachmentName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
   /// <summary>Whether the current state permits this action.</summary>
   public bool RefreshCommandAllowed() => !IsBusy;
   /// <summary>Whether the current state permits this action.</summary>
   public bool ApproveCommandAllowed() => !IsBusy && IsData && Detail?.CanDecide == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool RejectCommandAllowed() => !IsBusy && IsData && Detail?.CanDecide == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool SignStepCommandAllowed(ApprovalStepCardVm step) => step?.CanDecide == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool RejectStepCommandAllowed(ApprovalStepCardVm step) => step?.CanDecide == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool WithdrawCommandAllowed() => !IsBusy && Detail?.CanWithdraw == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool VoidCommandAllowed() => !IsBusy && Detail?.CanVoid == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool ReinstateCommandAllowed() => !IsBusy && Detail?.CanReinstate == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool CommentCommandAllowed() => !IsBusy && Detail is not null && !string.IsNullOrWhiteSpace(Comment);
   /// <summary>Whether the current state permits this action.</summary>
   public bool AttachCommandAllowed() => !IsBusy && Detail?.CanAttach == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool DownloadCommandAllowed(ta_ApprovalRequestAttachment row) => !IsBusy && row is not null;
   /// <summary>Whether the current state permits this action.</summary>
   public bool PreviewCommandAllowed() => !IsBusy && _editor?.HasPreview == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool SourceCommandAllowed() => !IsBusy && _editor?.HasSource == true;
   /// <summary>Whether the current state permits this action.</summary>
   public bool NextCommandAllowed() => !IsBusy && Page*(_payload.SystemViewer && _payload.ApprovalRequestId is null ? 1 : 50) < Total;
   /// <summary>Whether the current state permits this action.</summary>
   public bool PreviousCommandAllowed() => !IsBusy && Page > 1;
   /// <summary>Whether the host can offer the request manager.</summary>
   public static bool CanOpenManager(EmApp app) => app.IsDebugBypass || app.ActiveUser?.cUserIsAdmin == true;
   /// <summary>Bindable definitions for the approval view.</summary>
   public ObservableCollection<ApprovalDefinitionFilter> Definitions { get; } = [];
   /// <summary>Bindable rows for the approval view.</summary>
   public ObservableCollection<ta_ApprovalRequest> Rows { get; } = [];
   /// <summary>Bindable fields for the approval view.</summary>
   public ObservableCollection<ApprovalFieldVm> Fields { get; } = [];
   /// <summary>Bindable steps for the approval view.</summary>
   public ObservableCollection<ApprovalStepCardVm> Steps { get; } = [];
   /// <summary>Bindable timeline for the approval view.</summary>
   public ObservableCollection<string> Timeline { get; } = [];
   /// <summary>Bindable attachments for the approval view.</summary>
   public ObservableCollection<ta_ApprovalRequestAttachment> Attachments { get; } = [];
   /// <summary>Bindable module toolbar for the approval view.</summary>
   public ObservableCollection<ApprovalFileVm> Files { get; } = [];
   /// <summary>Chronological comments and signature decisions.</summary>
   public ObservableCollection<ApprovalCommentVm> Comments { get; } = [];
   /// <summary>Additional commands supplied by the owning module.</summary>
   public ObservableCollection<ApprovalToolbarItem> ModuleToolbar { get; } = [];
   /// <summary>Bindable selected definition for the approval view.</summary>
   public ApprovalDefinitionFilter? SelectedDefinition { get => Get<ApprovalDefinitionFilter?>(); set { if (Set(value) && !_loadingRows) { Page = 1; _ = RefreshAsync(); } } }
   /// <summary>Bindable selected request for the approval view.</summary>
   public ta_ApprovalRequest? SelectedRequest { get => Get<ta_ApprovalRequest?>(); set { if (Set(value) && !_loadingRows) { if (ShowDetails) _ = RunAsync(LoadDetailAsync); else RaiseState(); } } }
   /// <summary>Bindable detail for the approval view.</summary>
   public ApprovalRequestDetail? Detail { get => Get<ApprovalRequestDetail?>(); private set => Set(value); }
   /// <summary>Bindable module view for the approval view.</summary>
   public object? ModuleView { get => Get<object?>(); private set => Set(value); }
   /// <summary>Bindable error for the approval view.</summary>
   public string Error { get => Get(string.Empty); private set => Set(value); }
   /// <summary>Bindable is busy for the approval view.</summary>
   public new bool IsBusy { get => Get(false); private set { Set(value); RaiseState(); } }
   /// <summary>Bindable is not busy for the approval view.</summary>
   public new bool IsNotBusy => !IsBusy;
   /// <summary>Bindable is compact for the approval view.</summary>
   public bool IsCompact => _payload.Compact;
   /// <summary>Bindable show filters for the approval view.</summary>
   public bool ShowFilters => !IsCompact && !_payload.SystemViewer;
   /// <summary>Whether the current view embeds or opens one request's detail.</summary>
   public bool ShowDetails => !ShowFilters;
   /// <summary>Whether the full manager toolbar is visible.</summary>
   public bool ShowManagerToolbar => !_payload.SystemViewer || ShowFilteredPager;
   /// <summary>Whether the engine editor is browsing a waiting-work filter without a fixed request.</summary>
   public bool ShowFilteredPager => _payload.SystemViewer && _payload.ApprovalRequestId is null;
   /// <summary>Whether the signer card strip is visible.</summary>
   public bool IsSignersOpen { get => Get(true); set { Set(value); NotifyChanged(nameof(SignersToggleText)); } }
   /// <summary>Whether comments and attachments are visible.</summary>
   public bool IsSidePanelOpen { get => Get(true); set { Set(value); RaiseState(); } }
   /// <summary>Width reserved for the visible sidebar.</summary>
   public GridLength SideWidth => new(IsSidePanelOpen ? 340 : 0);
   /// <summary>Space separating the module view and sidebar.</summary>
   public GridLength SideGap => new(IsSidePanelOpen ? 14 : 0);
   /// <summary>Signer visibility caption including hidden progress.</summary>
   public string SignersToggleText => IsSignersOpen ? "Signers" : $"Signers · {Steps.Count(x => x.IsSigned)}/{Steps.Count}{(Steps.Any(x => x.IsMyTurn) ? " · your turn" : "")}";
   /// <summary>Sidebar visibility caption including comment count.</summary>
   public string SidePanelToggleText => IsSidePanelOpen ? "Comments" : $"Comments · {Comments.Count}";
   /// <summary>Time of the latest successful refresh.</summary>
   public string RefreshedText { get => Get(string.Empty); private set => Set(value); }
   /// <summary>Caption supplied by the module for source navigation.</summary>
   public string SourceCaption => _editor?.SourceCaption ?? "Open source";
   /// <summary>Readable state of the selected request.</summary>
   public string RequestStatus => Detail?.Header is { cApprovalRequestKind: ApprovalKind.System, cApprovalRequestStage: ApprovalStage.Approved } ? "Completed" : Detail?.Header.cApprovalRequestStage.ToString() ?? string.Empty;
   /// <summary>Bindable is data for the approval view.</summary>
   public bool IsData => Detail?.Header.cApprovalRequestKind == ApprovalKind.Data;
   /// <summary>Bindable is system for the approval view.</summary>
   public bool IsSystem => Detail?.Header.cApprovalRequestKind == ApprovalKind.System;
   /// <summary>Bindable has preview for the approval view.</summary>
   public bool HasPreview => _editor?.HasPreview == true;
   /// <summary>Bindable has source for the approval view.</summary>
   public bool HasSource => _editor?.HasSource == true;
   /// <summary>Selected approval and its current status.</summary>
   public string Title => Detail is null ? "Select an approval request" : $"{Detail.Header.cApprovalName} · {Detail.Header.cApprovalRequestStage}";
   /// <summary>Module-supplied subject summary.</summary>
   public string Summary => Detail?.Header.cSummary ?? string.Empty;
   /// <summary>Optional second summary line supplied by the module.</summary>
   public string SubSummary => Detail?.Header.cSubSummary ?? string.Empty;
   /// <summary>Whether the second summary line is shown.</summary>
   public bool HasSubSummary => !string.IsNullOrWhiteSpace(Detail?.Header.cSubSummary);
   /// <summary>Complete proposed objects awaiting insertion.</summary>
   public string CreateSnapshots { get => Get(string.Empty); private set => Set(value); }
   /// <summary>Bindable note for the approval view.</summary>
   public string Note { get => Get(string.Empty); set => Set(value); }
   /// <summary>Bindable comment for the approval view.</summary>
   public string Comment { get => Get(string.Empty); set { Set(value); RaiseState(); } }
   /// <summary>Bindable search for the approval view.</summary>
   public string Search { get => Get(string.Empty); set => Set(value); }
   /// <summary>Bindable waiting for me for the approval view.</summary>
   public bool WaitingForMe { get => Get(true); set => Set(value); }
   /// <summary>Bindable stage for the approval view.</summary>
   public ApprovalStage? Stage { get => Get<ApprovalStage?>(); set => Set(value); }
   /// <summary>Bindable stages for the approval view.</summary>
   public ApprovalStage?[] Stages { get; } = [null, .. Enum.GetValues<ApprovalStage>()];
   /// <summary>Bindable page for the approval view.</summary>
   public int Page { get => Get(1); private set => Set(value); }
   /// <summary>Bindable total for the approval view.</summary>
   public int Total { get => Get(0); private set => Set(value); }
   /// <summary>Bindable page label for the approval view.</summary>
   public string PageLabel => $"Page {Page} · {Total} requests";
   /// <summary>Applies navigation filters before loading.</summary>
   public void Configure(object? input) {
      _released = false;
      _payload = input as ApprovalManagerNavigationPayload ?? new() {
         WaitingForMeOnly = !(EmApp?.IsDebugBypass == true || EmApp?.ActiveUser?.cUserIsAdmin == true)
      };
      if (input is string text && text.IndexOf(':') is var split && split > 0 && Enum.TryParse<ApprovalKind>(text[..split], out var kind)) {
         _payload.Kind = kind; _payload.Name = text[(split+1)..];
      }
      WaitingForMe = _payload.WaitingForMeOnly; Page = 1; RaiseState();
   }
   /// <summary>Releases the current module view and invalidates pending loads.</summary>
   public void Release() { _released = true; _version++; ModuleView = null; _editor = null; }
   private void RaiseState() {
      foreach (var property in new[] { nameof(IsNotBusy), nameof(IsCompact), nameof(ShowFilters), nameof(ShowDetails), nameof(IsData), nameof(IsSystem), nameof(Title), nameof(Summary), nameof(SubSummary), nameof(HasSubSummary),nameof(PageLabel), nameof(HasPreview), nameof(HasSource), nameof(ShowManagerToolbar), nameof(ShowFilteredPager), nameof(SideWidth), nameof(SideGap), nameof(SignersToggleText), nameof(SidePanelToggleText), nameof(SourceCaption), nameof(RequestStatus) }) NotifyChanged(property);
      NotifyChanged(nameof(BatchSigningAvailable));
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
      foreach (var step in Steps) step.RefreshState();
   }
   private async Task RunAsync(Func<Task> work) {
      if (IsBusy || _released) return;
      IsBusy = true; Error = string.Empty;
      try { await work(); } catch (Exception exception) { Error = exception.Message; }
      finally { IsBusy = false; }
   }
   /// <summary>Bindable refresh async for the approval view.</summary>
   public Task RefreshAsync() => RunAsync(async () => {
      if (!_payload.Compact && !_payload.SystemViewer && !CanOpenManager(EmApp!)) {
         Rows.Clear(); Error = "Approval Manager is available to administrators"; return;
      }
      var version = ++_version;
      var definitions = await Service.GetMeta_ApprovalCapabilities();
      SetBatchCapabilities(definitions);
      if (_released || version != _version) return;
      _loadingRows = true;
      try {
         var selected = SelectedDefinition;
         var kind = selected is null ? _payload.Kind : selected.Kind;
         var name = selected is null ? _payload.Name : selected.Name;
         Definitions.Clear();
         Definitions.Add(new(null, null, "All approvals"));
         foreach (var definition in definitions.Where(x => x.CanView)) Definitions.Add(new(definition.Kind, definition.Name, definition.Title));
         SelectedDefinition = Definitions.FirstOrDefault(x => x.Kind == kind && x.Name == name) ?? Definitions[0];
         var oldId = SelectedRequest?.cApprovalRequestId;
         var result = await Service.PostGetMeta_ApprovalRequests(new ApprovalQuery(ShowFilters ? SelectedDefinition.Kind : _payload.Kind,
            ShowFilters ? SelectedDefinition.Name : _payload.Name, _payload.SubjectKey, Stage, WaitingForMe, Search, Page, _payload.SystemViewer && _payload.ApprovalRequestId is null ? 1 : 50, OldestFirst: _payload.SystemViewer && _payload.ApprovalRequestId is null));
         if (_released || version != _version) return;
         Rows.Clear(); foreach (var row in result.Items) Rows.Add(row); Total = result.Total;
         SelectedRequest = Rows.FirstOrDefault(x => x.cApprovalRequestId == (_payload.ApprovalRequestId ?? oldId)) ?? Rows.FirstOrDefault();
         if (_payload.ApprovalRequestId is { } direct && SelectedRequest?.cApprovalRequestId != direct) {
            var detail = await Service.GetMeta_ApprovalRequest(direct); Rows.Insert(0, detail.Header); SelectedRequest = detail.Header;
         }
         if (ShowDetails) await LoadDetailAsync();
         if (ShowFilteredPager && Rows.Count == 0) Error = "No approval requests match the current filters.";
         RefreshedText = $"Refreshed {DateTime.Now:dd-MMM-yyyy HH:mm}";
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
   /// <summary>Bindable user name for the approval view.</summary>
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
      var replacement = (await Service.GetMeta_ApprovalRequest(id)).Header;
      if (_payload.SystemViewer) {
         await LoadDetailAsync();
         await NavigateToRequestAsync(replacement);
         return;
      }
      _loadingRows = true; SelectedRequest = replacement; _loadingRows = false;
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
