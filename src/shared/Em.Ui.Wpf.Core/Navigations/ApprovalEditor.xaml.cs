using System.Windows.Controls;
using System.Windows.Input;
namespace Em.Ui.Wpf.Navigations;
/// <summary>System shell surrounding the registered module-owned approval view.</summary>
public partial class ApprovalEditor : UserControl {
   /// <summary>Creates the shell; its data context comes from the owning Approval Manager.</summary>
   public ApprovalEditor() { InitializeComponent(); }
   private void OnSignersWheel(object sender, MouseWheelEventArgs args) {
      if (sender is not ScrollViewer scroller) return;
      scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - args.Delta);
      args.Handled = true;
   }
}
