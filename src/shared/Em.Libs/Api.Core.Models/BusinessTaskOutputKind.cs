namespace Em.Api.Core.Models
{
   /// <summary>
   /// Bentuk hasil yang ditinggalkan sebuah business task setelah sukses.
   /// </summary>
   public enum BusinessTaskOutputKind
   {
      /// <summary>
      /// Tanpa hasil yang perlu diambil: pekerjaannya sendiri adalah hasilnya (mis. membuat archive).
      /// Task seperti ini hilang sendiri tidak lama setelah sukses.
      /// </summary>
      None = 0,

      /// <summary>Hasilnya data JSON, diambil lewat action hasil JSON.</summary>
      Json = 1,

      /// <summary>Hasilnya sebuah file (mis. Excel), diunduh lewat action hasil file.</summary>
      File = 2
   }
}
