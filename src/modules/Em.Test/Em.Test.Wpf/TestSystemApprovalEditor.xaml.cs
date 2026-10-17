using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;

namespace Em.Test.Wpf;

/// <summary>Example of a module-owned main view in the engine's System approval shell.</summary>
public partial class TestSystemApprovalEditor : UserControl, IEmApprovalSystemEditor {
   private readonly ITestServices _services;
   private readonly EmApp _app;
   private ApprovalSystemEditorContext? _context;
   /// <summary>Creates the view with the module client and the active host.</summary>
   public TestSystemApprovalEditor(ITestServices services, EmApp app) {
      _services = services; _app = app; InitializeComponent(); DataContext = Vm;
      Vm.RegisterCommand(nameof(TestSystemApprovalVm.HelpCommand), Vm.HelpCommand);
   }
   /// <summary>Module view state.</summary>
   public TestSystemApprovalVm Vm { get; } = new();
   /// <inheritdoc />
   public async Task LoadAsync(ApprovalSystemEditorContext context, CancellationToken ct = default) {
      _context = context; var row = await _services.GetTa_TestDoc_ById(context.State.Header.cSubjectKey!);
      if (row is null) throw new InvalidOperationException("The test document no longer exists.");
      Vm.Number = row.cTestDocNo; Vm.Title = row.cTestDocTitle; Vm.Amount = row.cTestDocAmount;
      Vm.Passed = row.cTestDocQaPassed == true; Vm.Remarks = row.cTestDocQaRemarks ?? string.Empty;
      Vm.CanEditQa = context.State.Steps.Any(x => x.cApprovalRequestStepName == "QA Check" && context.State.DecidableStepIds.Contains(x.cApprovalRequestStepId));
   }
   /// <inheritdoc />
   public async Task<ApprovalDecisionCheck> ValidateSignAsync(ta_ApprovalRequestStep step, CancellationToken ct = default) {
      if (step.cApprovalRequestStepName == "QA Check") {
         if (Vm.Amount <= 0) return new(false, "The amount must be positive before QA can sign.");
         if (!Vm.Passed && string.IsNullOrWhiteSpace(Vm.Remarks)) return new(false, "Provide QA remarks when the check did not pass.");
         await _services.PostMeta_TestDocQa(_context!.State.Header.cSubjectKey!, Vm.Passed, Vm.Remarks);
      }
      return new(true);
   }
   /// <inheritdoc />
   public Task<ApprovalDecisionCheck> ValidateRejectAsync(ta_ApprovalRequestStep step, CancellationToken ct = default) => Task.FromResult(new ApprovalDecisionCheck(true));
   /// <inheritdoc />
   public bool HasPreview => true;
   /// <inheritdoc />
   public async Task<Stream?> GetPreviewAsync(CancellationToken ct = default) => await _services.GetMeta_TestDocPdf(_context!.State.Header.cSubjectKey!);
   /// <inheritdoc />
   public bool HasSource => true;
   /// <inheritdoc />
   public Task OpenSourceAsync() => _app.NavigateTo("test.docs", _context!.State.Header.cSubjectKey);
   /// <inheritdoc />
   public IReadOnlyList<ApprovalToolbarItem> ToolbarItems => [new("Module help", Vm.Commands[nameof(TestSystemApprovalVm.HelpCommand)]!)];
   /// <inheritdoc />
   public async Task OnReinstatedAsync(string oldId, string newId, string key) {
      var row = await _services.GetTa_TestDoc_ById(key) ?? throw new InvalidOperationException("The test document no longer exists.");
      var json = JsonNode.Parse(row.json_object ?? "{}")!.AsObject(); json["approvalRequestId"] = newId;
      row.json_object = json.ToJsonString();
      // The module's normal edit route accepts only drafts; a dedicated link update is used here.
      await _services.PostMeta_TestDocApprovalLink(key, newId);
   }
}
/// <summary>Sample module fields; business input remains outside the engine decision payload.</summary>
public sealed class TestSystemApprovalVm : MvvmModelBase {
   /// <summary>Document number.</summary>
   public string Number { get => Get(string.Empty); set => Set(value); }
   /// <summary>Document title.</summary>
   public string Title { get => Get(string.Empty); set => Set(value); }
   /// <summary>Amount used by module validation.</summary>
   public decimal Amount { get => Get(0m); set => Set(value); }
   /// <summary>QA result.</summary>
   public bool Passed { get => Get(false); set => Set(value); }
   /// <summary>QA remarks.</summary>
   public string Remarks { get => Get(string.Empty); set => Set(value); }
   /// <summary>Whether this request permits QA editing.</summary>
   public bool CanEditQa { get => Get(false); set { Set(value); NotifyChanged(nameof(QaReadOnly)); } }
   /// <summary>Read-only state for the remarks input.</summary>
   public bool QaReadOnly => !CanEditQa;
   /// <summary>Module-owned help text.</summary>
   public string Message { get => Get(string.Empty); private set => Set(value); }
   /// <summary>Example of an extra module toolbar command.</summary>
   public void HelpCommand() => Message = "QA input is saved by the test module before its signature. The engine owns decisions, signer snapshots and verification codes.";
}
