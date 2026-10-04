namespace Em.Ui.Core.Shared
{
   /// <summary>Keterangan sebuah perpindahan navigasi, diteruskan ke body dan ke pendengar router.</summary>
   public class NavigationEventArgs : EventArgs
   {
      /// <summary>Navigasi tujuan perpindahan ini.</summary>
      public required INavigation  NavigationItem { get; init; }

      /// <summary>
      /// Entri milik body yang menerima callback ini - untuk <c>OnNavigatingAway</c> itu entri yang
      /// sedang ditinggalkan, untuk callback lain entri tujuannya. Lewat entri inilah body mengganti
      /// judulnya, menutup dirinya, atau membuka layar lain.
      /// </summary>
      public required INavigationEntry Entry { get; init; }

      /// <summary>Parameter untuk navigasi tujuan, atau <c>null</c> kalau tidak ada.</summary>
      public object? Data { get; set; }
   }

}
