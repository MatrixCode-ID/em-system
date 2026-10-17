using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
namespace Em.Ui.Wpf.Dialogs;

/// <summary>A themed, reviewable confirmation with an optional or required decision note.</summary>
public partial class ApprovalDecisionDialog : EmWindow {
   /// <summary>Creates a decision dialog; confirmation never sends a request by itself.</summary>
   public ApprovalDecisionDialog(string action, string subtitle, bool reasonRequired) {
      InitializeComponent();
      Vm = new ApprovalDecisionDialogVm(action, subtitle, reasonRequired);
      Vm.Confirmed += () => DialogResult = true; DataContext = Vm;
   }
   /// <summary>Dialog input state.</summary>
   public ApprovalDecisionDialogVm Vm { get; }
}
/// <summary>Decision input; a rejection or void cannot confirm with an empty reason.</summary>
public sealed class ApprovalDecisionDialogVm : MvvmModelBase {
   private readonly bool _reasonRequired;
   /// <summary>Creates the dialog state and confirmation command.</summary>
   public ApprovalDecisionDialogVm(string action, string subtitle, bool reasonRequired) {
      Action = action; Subtitle = subtitle; _reasonRequired = reasonRequired;
      RegisterCommand(nameof(ConfirmCommand), ConfirmCommand, () => !_reasonRequired || !string.IsNullOrWhiteSpace(Note));
   }
   /// <summary>Raised when valid input is confirmed.</summary>
   public event System.Action? Confirmed;
   /// <summary>Button action.</summary>
   public string Action { get; }
   /// <summary>Dialog title.</summary>
   public string Title => $"{Action} approval";
   /// <summary>Context and consequences for this action.</summary>
   public string Subtitle { get; }
   /// <summary>Field caption.</summary>
   public string NoteCaption => _reasonRequired ? "Reason" : "Decision note";
   /// <summary>Input hint.</summary>
   public string Hint => _reasonRequired ? "A reason is required and stays in the approval history." : "The note is optional and stays with your signature.";
   /// <summary>Unmodified TwoWay input.</summary>
   public string Note { get => Get(string.Empty); set { Set(value); Commands[nameof(ConfirmCommand)]?.RaiseCanExecuteChanged(); } }
   /// <summary>Confirms the local input; the caller performs the server action afterward.</summary>
   public void ConfirmCommand() => Confirmed?.Invoke();
}
