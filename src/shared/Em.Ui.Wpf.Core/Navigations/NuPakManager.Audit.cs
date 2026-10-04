using System.Windows;
using Em.Api.Core.Models;
namespace Em.Ui.Wpf.Navigations;
public partial class NuPakManager
{
   private Task RefreshAudit() => Run("audit", async () => {
      audit.IsEnabled = false;
      try { var f=FeedId;var generation=_generation;var global=serverAudit.IsChecked==true;var filter=new NuPakAuditFilter {
         Package = auditPackage.Text, Version = auditVersion.Text, Actor = auditActor.Text, Action = auditAction.Text, Result = auditResult.Text,
         From = auditFrom.SelectedDate, To = auditTo.SelectedDate?.AddDays(1).AddTicks(-1) };
         var rows=global?await _service!.GetMeta_NuPakServerAudit(filter,_auditSkip,100):f is null?[]:await _service!.GetMeta_NuPakAudit(f,filter,_auditSkip,100);
         if(generation==_generation&&global==(serverAudit.IsChecked==true))audit.ItemsSource=rows;
      }
      finally { audit.IsEnabled = true; }
   });
   private async void AuditRefresh(object s, RoutedEventArgs e) => await RefreshAudit();
   private async void AuditFilterClick(object s, RoutedEventArgs e) { _auditSkip = 0; await RefreshAudit(); }
   private async void AuditPrevious(object s, RoutedEventArgs e) { _auditSkip = Math.Max(0, _auditSkip - 100); await RefreshAudit(); }
   private async void AuditNext(object s, RoutedEventArgs e) { if (audit.Items.Count < 100) return; _auditSkip += 100; await RefreshAudit(); }
}
