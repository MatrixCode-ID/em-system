using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>The shared contract of the WPF login screen.</summary>
   public interface ILoginScreen : INavigationBody
   {
      /// <summary>The view model of the login screen.</summary>
      LoginControlVm Vm { get; }
   }
}
