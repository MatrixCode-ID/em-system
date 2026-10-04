namespace Em.Shared
{
   /// <summary>
   /// Daftar HTTP method yang dikenali oleh dispatcher <c>EmApp</c> untuk menentukan
   /// cara sebuah action diakses. Saat ini action hanya bisa didaftarkan sebagai
   /// <see cref="Get"/> (lewat <c>[GetAction]</c>) atau <see cref="Post"/> (lewat <c>[PostAction]</c>);
   /// nilai lain disediakan untuk kebutuhan masa depan.
   /// </summary>
   public enum HttpMethod
   {
      /// <summary>Request dikirim lewat HTTP POST, dengan body JSON.</summary>
      Post,

      /// <summary>Request dikirim lewat HTTP GET, dengan parameter lewat query string.</summary>
      Get,

      /// <summary>HTTP PUT. Belum dipakai oleh dispatcher saat ini.</summary>
      Put,

      /// <summary>HTTP DELETE. Belum dipakai oleh dispatcher saat ini.</summary>
      Delete,

      /// <summary>HTTP HEAD. Belum dipakai oleh dispatcher saat ini.</summary>
      Head,
   }
}
