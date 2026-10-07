using Em.Api.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Core;

internal class ApprovalAccessCatalog(EmApp app)
{
   private string? _userId;
   private bool _canOpen;
   public bool CanOpen => app.IsDebugBypass || app.ActiveUser?.cUserIsAdmin == true ||
      (app.ActiveUser?.cUserId == _userId && _canOpen);

   public async Task LoadAsync() {
      var userId = app.ActiveUser?.cUserId;
      _userId = userId; _canOpen = false;
      if (userId == null) return;
      try {
         var types = await app.ServiceProvider.GetRequiredService<IApprovalServices>().GetMeta_ApprovalDocumentTypes();
         if (app.ActiveUser?.cUserId == userId) _canOpen = types.Length > 0;
      }
      catch { /* An unavailable access catalog keeps the tool hidden for ordinary users. */ }
   }
}
