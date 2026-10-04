namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Jenis sebuah navigasi, wajib dinyatakan setiap kali navigasi didaftarkan. Jenis ini
   /// menggolongkan layar menurut perannya, bukan menurut tampilannya.
   /// </summary>
   public enum NavigationKind
   {
      /// <summary>
      /// Layar pengelola: daftar, pencarian, atau alat yang menjadi titik awal pekerjaan - mis.
      /// daftar pengguna. Biasanya dibuka dari menu dan tidak membawa parameter.
      /// </summary>
      Manager,

      /// <summary>
      /// Layar penyunting satu dokumen atau satu data - mis. penyunting satu pengguna. Biasanya
      /// dibuka dari layar pengelola dengan parameter yang menunjuk dokumennya.
      /// </summary>
      Editor
   }
}
