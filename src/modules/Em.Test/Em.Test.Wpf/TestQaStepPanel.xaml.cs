using System.Text.Json;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// The input panel of the QA Check step in the Approval Manager. It shows the contract of a step panel:
   /// the panel writes its payload to <see cref="IApprovalPanelHost.InputPayload"/> and declares its
   /// validity through <see cref="IApprovalPanelHost.IsInputValid"/>; the Approval Manager sends it
   /// together with the decision.
   /// </summary>
   public partial class TestQaStepPanel : UserControl
   {
      public TestQaStepPanel() {
         InitializeComponent();
      }
   }

   public class TestQaStepPanelVm : MvvmModelBase, IApprovalPanel
   {
      public IApprovalPanelHost Host { get; set; } = null!;

      public bool Passed {
         get => Get(true);
         set => Set(value, _ => Push());
      }

      public string Remarks {
         get => Get(string.Empty) ?? string.Empty;
         set => Set(value, _ => Push());
      }

      public string Hint => Passed || !string.IsNullOrWhiteSpace(Remarks)
         ? "This input is stamped onto the PDF when you sign."
         : "Remarks are required when the document did not pass.";

      public Task LoadAsync() {
         Push();
         return Task.CompletedTask;
      }

      private void Push() {
         NotifyChanged(nameof(Hint));
         if (Host is null) return;

         Host.InputPayload = JsonSerializer.Serialize(new TestQaPayload {
            Passed = Passed,
            Remarks = string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim()
         });
         Host.IsInputValid = Passed || !string.IsNullOrWhiteSpace(Remarks);
      }
   }
}
