using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>Kontrak bersama layar login WPF.</summary>
   public interface ILoginScreen : INavigationBody
   {
      /// <summary>ViewModel layar login.</summary>
      LoginControlVm Vm { get; }
   }
}
