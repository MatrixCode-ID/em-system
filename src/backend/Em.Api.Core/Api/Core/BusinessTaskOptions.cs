using Em.Api.Core.Models;

namespace Em.Api.Core
{
   /// <summary>
   /// Keterangan sebuah business task yang hendak dimulai lewat <c>ServicesBase.StartBusinessTask</c>.
   /// </summary>
   public sealed class BusinessTaskOptions
   {
      /// <summary>
      /// Kunci pekerjaan. Selama task berkunci ini masih antri atau berjalan, memulai kunci yang sama
      /// ditolak 409. Untuk <see cref="BusinessTaskScope.Global"/> kuncinya unik di seluruh server, untuk
      /// <see cref="BusinessTaskScope.Personal"/> unik per user - dua user boleh menjalankan kunci yang sama
      /// bersamaan. Tidak membedakan huruf besar/kecil. Pakai awalan nama module supaya tidak bentrok
      /// dengan module lain, mis. <c>"sales:invoice-load:2025"</c>.
      /// </summary>
      public required string Key { get; init; }

      /// <summary>Judul yang ditampilkan ke user, mis. "Load invoices 2025".</summary>
      public required string Title { get; init; }

      /// <summary>
      /// Milik siapa task ini. <see cref="BusinessTaskScope.Personal"/> tampil di daftar task pribadi
      /// pemulainya, dan hanya ia atau administrator yang boleh mengurusnya.
      /// <see cref="BusinessTaskScope.Global"/> milik layar module yang memintanya: statusnya ditanyakan
      /// lewat action module itu sendiri (dengan <c>FindBusinessTask</c>), dan siapa pun yang lolos hak
      /// action tersebut boleh membatalkan atau membersihkannya. Sengaja tanpa nilai bawaan.
      /// </summary>
      public required BusinessTaskScope Scope { get; init; }

      /// <summary>
      /// Nama navigasi yang disarankan untuk membuka hasil JSON task ini di client, kalau ada.
      /// </summary>
      public string? NavigationName { get; init; }

      /// <summary>Nama file hasil yang disarankan saat diunduh. Wajib untuk task yang hasilnya file.</summary>
      public string? ResultFileName { get; init; }

      /// <summary>Jenis isi (MIME type) file hasil, mis. <c>application/vnd.openxmlformats-officedocument.spreadsheetml.sheet</c>.</summary>
      public string? ResultContentType { get; init; }
   }
}
