using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Definisi sebuah layar yang bisa dibuka lewat namanya: judul bawaan, tipe body, letak di menu,
   /// dan ikatan hak aksesnya. Definisi ini tidak memegang body maupun data apa pun - setiap kali
   /// layarnya dibuka, yang tercipta adalah sebuah <see cref="INavigationEntry"/>, sehingga satu
   /// definisi bisa tampil beberapa kali sekaligus dengan data yang berbeda.
   /// </summary>
   public interface INavigation
   {
      /// <summary>Nama unik navigasi, yaitu yang dicari <c>NavigateTo(string)</c>.</summary>
      string Name { get; }

      /// <summary>
      /// Judul bawaan layar. Dipakai sebagai judul entri kalau parameter yang dibawa tidak
      /// menyediakan judulnya sendiri (lihat <see cref="NavigationPayloadBase.Title"/>).
      /// </summary>
      string Title { get; set; }

      /// <summary>Keterangan singkat di bawah judul.</summary>
      string Subtitle { get; set; }

      /// <summary>Penjelasan panjang, dipakai kartu menu di home.</summary>
      string Description { get; set; }

      /// <summary>Letak layar ini di pohon menu home, atau <c>null</c> kalau ia tidak lewat menu.</summary>
      MenuPath? MenuPath { get; set; }

      /// <summary>Router aplikasi pemilik navigasi ini.</summary>
      INavigationHost NavigationHost { get; }

      /// <summary>Tipe control yang dibangun sebagai body setiap kali layar ini dibuka.</summary>
      IBodyType BodyType { get; }

      /// <summary>Apakah layar ini menolak dibuka tanpa parameter.</summary>
      bool RequireParameter { get; }

      /// <summary>Urutan layar ini di menu home; semakin kecil semakin di depan.</summary>
      int OrderIndex { get; }

      /// <summary>
      /// Jenis layar ini, <see cref="NavigationKind.Manager"/> atau <see cref="NavigationKind.Editor"/>.
      /// Wajib diisi saat navigasi didaftarkan.
      /// </summary>
      NavigationKind Kind { get; }

      /// <summary>
      /// Nama module pemilik navigasi ini, diisi lewat overload <c>AddNavigation</c> yang berikat
      /// module - atau <c>null</c> kalau navigasi ini didaftarkan tanpa ikatan sama sekali dan
      /// karena itu terbuka untuk siapa pun yang sudah masuk. Dipakai <see cref="NavigationAccess"/>,
      /// bukan diisi langsung oleh module.
      /// </summary>
      string? ModuleName { get; }

      /// <summary>
      /// Claim yang wajib dimiliki user untuk membuka navigasi ini, diisi lewat overload
      /// <c>AddNavigation</c> yang berklaim - atau <c>null</c> kalau navigasi ini cukup terikat
      /// module tanpa claim tertentu. Dipakai <see cref="NavigationAccess"/>, bukan diisi langsung
      /// oleh module.
      /// </summary>
      ClaimAction? RequiredClaim { get; }
   }
}
