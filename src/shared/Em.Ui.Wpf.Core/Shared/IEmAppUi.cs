using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// The contract of the WPF-specific application object on top of the base contract
   /// <see cref="IEmApp"/>. It adds no members yet, but is still registered in the DI container as a place
   /// for needs that only exist on the WPF side.
   /// </summary>
   public interface IEmAppUi : IEmApp
   {
   }
}
