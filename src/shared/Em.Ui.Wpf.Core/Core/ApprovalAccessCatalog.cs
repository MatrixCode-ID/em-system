namespace Em.Ui.Wpf.Core;
internal class ApprovalAccessCatalog(EmApp app) {
   public bool CanOpen => app.IsDebugBypass || app.ActiveUser?.cUserIsAdmin == true;
   public Task LoadAsync() => Task.CompletedTask;
}
