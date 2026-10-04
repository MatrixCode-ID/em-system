namespace Em.Shared
{
   /// <summary>
   /// Amplop (envelope) hasil eksekusi satu action pada dispatcher <c>EmApp</c>.
   /// Semua response API dibungkus dalam bentuk ini dan diserialisasi sebagai JSON,
   /// baik untuk hasil sukses maupun error, supaya bentuk response selalu konsisten
   /// di seluruh action pada seluruh module.
   /// </summary>
   public class ActionResult
   {
      /// <summary>
      /// Menandakan apakah <see cref="Data"/> berisi nilai yang valid untuk dibaca.
      /// </summary>
      public bool HasData { get; set; }

      /// <summary>
      /// Nilai hasil (return value) dari action yang dipanggil. Bisa <c>null</c> jika action
      /// tidak mengembalikan data atau gagal dieksekusi.
      /// </summary>
      public object? Data { get; set; }

      /// <summary>
      /// <c>true</c> jika action berhasil dieksekusi tanpa error; <c>false</c> jika terjadi
      /// kegagalan (mis. binding parameter gagal, action tidak ditemukan, atau exception saat invoke).
      /// </summary>
      public bool ValidResult { get; set; }

      /// <summary>
      /// Pesan error singkat jika <see cref="ValidResult"/> bernilai <c>false</c>.
      /// </summary>
      public string? ErrorMessage { get; set; }

      /// <summary>
      /// Pesan tambahan/informasi lain terkait hasil eksekusi, di luar pesan error utama.
      /// </summary>
      public string? Messages { get; set; }

      /// <summary>
      /// Stack trace exception, jika terjadi error tak tertangani saat eksekusi action.
      /// </summary>
      public string? ErrorStackTrace { get; set; }

      /// <summary>
      /// Nama lengkap (assembly-qualified atau full name) dari tipe hasil <see cref="Data"/>,
      /// dipakai klien untuk mengetahui cara mendeserialisasi <see cref="Data"/> dengan benar.
      /// </summary>
      public string ResultTypeFullName { get; set; } = "";

      /// <summary>
      /// Kode status HTTP yang merepresentasikan hasil eksekusi (mis. 200, 400, 404, 500).
      /// </summary>
      public int StatusCode { get; set; }

      /// <summary>
      /// Nama lengkap tipe service (class) yang menangani action ini, untuk keperluan diagnostik.
      /// </summary>
      public string ServiceType { get; set; } = "";

      /// <summary>
      /// Nama action/method yang dipanggil pada service tersebut, untuk keperluan diagnostik.
      /// </summary>
      public string ServiceAction { get; set; } = "";
   }
}
