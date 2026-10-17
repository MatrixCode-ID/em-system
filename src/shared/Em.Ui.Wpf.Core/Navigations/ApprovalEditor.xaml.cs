using System.Windows.Controls;
namespace Em.Ui.Wpf.Navigations;
/// <summary>System shell surrounding the registered module-owned approval view.</summary>
public partial class ApprovalEditor : UserControl {
   /// <summary>Creates the shell; its data context comes from the owning Approval Manager.</summary>
   public ApprovalEditor() { InitializeComponent(); }
}
