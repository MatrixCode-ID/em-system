using System.Collections.ObjectModel;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Koleksi <see cref="UiCommandBase"/> milik satu <see cref="MvvmModelBase"/>, dengan
   /// pencarian tambahan berdasarkan nama command.
   /// </summary>
   public class UiCommandBaseCollection : Collection<UiCommandBase>
   {
      /// <summary>
      /// Mengambil command berdasarkan namanya.
      /// </summary>
      /// <param name="name">Nama command yang dicari.</param>
      /// <returns>Command dengan nama yang cocok, atau <c>null</c> jika tidak ditemukan.</returns>
      public UiCommandBase? this[string name] => this.SingleOrDefault(command => command.Name == name);
   }
}
