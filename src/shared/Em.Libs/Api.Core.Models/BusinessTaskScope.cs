namespace Em.Api.Core.Models
{
   /// <summary>
   /// Milik siapa sebuah business task, dan karena itu di mana ia tampil. Dipilih penulis action saat
   /// memulai task.
   /// </summary>
   public enum BusinessTaskScope
   {
      /// <summary>
      /// Milik user yang memulainya, misalnya memuat data untuk dirinya sendiri. Tampil di daftar task
      /// pribadi user itu di jendela utama. Yang boleh membatalkan, membersihkan, dan mengambil hasilnya
      /// hanya pemiliknya dan administrator.
      /// </summary>
      Personal = 0,

      /// <summary>
      /// Milik layar module yang memintanya, misalnya membuat archive di CDN. Tampil di layar itu untuk
      /// siapa pun yang boleh membukanya, dan siapa pun yang lolos hak action module tersebut boleh
      /// membatalkan atau membersihkannya.
      /// </summary>
      Global = 1
   }
}
