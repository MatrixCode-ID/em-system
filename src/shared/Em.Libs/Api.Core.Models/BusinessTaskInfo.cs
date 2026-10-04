using System.Text.Json.Serialization;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Potret satu business task seperti yang dilaporkan server: apa pekerjaannya, milik siapa, sudah
   /// sampai mana, dan apa yang boleh dilakukan pemanggil terhadapnya. Nilainya berubah selama task
   /// berjalan, jadi layar yang memantaunya memuat ulang potret ini secara berkala.
   /// </summary>
   public class BusinessTaskInfo
   {
      /// <summary>Id unik task ini (ULID), dipakai untuk membatalkan, membersihkan, atau mengambil hasilnya.</summary>
      public string Id { get; set; } = "";

      /// <summary>
      /// Kunci pekerjaan yang dipilih penulis action. Selama sebuah task masih hidup, task lain dengan
      /// kunci yang sama tidak bisa dimulai: untuk task global kuncinya unik di seluruh server, untuk task
      /// personal unik per pemilik.
      /// </summary>
      public string Key { get; set; } = "";

      /// <summary>Milik siapa task ini, dan karena itu di mana ia tampil.</summary>
      public BusinessTaskScope Scope { get; set; }

      /// <summary>Judul yang ditampilkan ke user, mis. "Archive photos.zip".</summary>
      public string Title { get; set; } = "";

      /// <summary>Module yang memulai task ini.</summary>
      public string ModuleName { get; set; } = "";

      /// <summary>Id user yang memulai task ini.</summary>
      public string OwnerUserId { get; set; } = "";

      /// <summary>Nama user yang memulai task ini, untuk ditampilkan.</summary>
      public string OwnerName { get; set; } = "";

      /// <summary>Bentuk hasil yang ditinggalkan task ini setelah sukses.</summary>
      public BusinessTaskOutputKind OutputKind { get; set; }

      /// <summary>Tahap yang sedang dijalani task ini.</summary>
      public BusinessTaskStatus Status { get; set; }

      /// <summary>
      /// Kemajuan dalam persen (0–100), atau <c>null</c> kalau kemajuannya tidak bisa diukur. Tampilkan
      /// progress bar tak tentu untuk nilai <c>null</c>.
      /// </summary>
      public double? Percent { get; set; }

      /// <summary>Keterangan singkat langkah yang sedang dikerjakan, mis. nama file yang sedang diproses.</summary>
      public string Caption { get; set; } = "";

      /// <summary>Kapan task ini dimulai dan masuk antrian.</summary>
      public DateTimeOffset QueuedAt { get; set; }

      /// <summary>Kapan task ini benar-benar mulai dikerjakan; <c>null</c> selama masih antri.</summary>
      public DateTimeOffset? StartedAt { get; set; }

      /// <summary>Kapan task ini selesai, apa pun hasilnya; <c>null</c> selama masih hidup.</summary>
      public DateTimeOffset? FinishedAt { get; set; }

      /// <summary>Pesan kesalahan kalau task ini gagal.</summary>
      public string? ErrorMessage { get; set; }

      /// <summary>
      /// Nama navigasi yang disarankan penulis action untuk membuka hasil JSON task ini, kalau ada.
      /// </summary>
      public string? NavigationName { get; set; }

      /// <summary>Nama file hasil, untuk task yang hasilnya berupa file.</summary>
      public string? ResultFileName { get; set; }

      /// <summary>Jenis isi (MIME type) file hasil, kalau penulis action menyebutkannya.</summary>
      public string? ResultContentType { get; set; }

      /// <summary>Ukuran hasil dalam byte, terisi setelah task yang punya hasil sukses.</summary>
      public long? ResultSize { get; set; }

      /// <summary><c>true</c> kalau pemanggil boleh membatalkan task ini (dan task-nya masih hidup).</summary>
      public bool CanCancel { get; set; }

      /// <summary><c>true</c> kalau pemanggil boleh membersihkan task ini (dan task-nya sudah selesai).</summary>
      public bool CanClear { get; set; }

      /// <summary><c>true</c> kalau pemanggil boleh mengambil hasil task ini.</summary>
      public bool CanReadResult { get; set; }

      /// <summary><c>true</c> selama task ini masih antri atau berjalan.</summary>
      [JsonIgnore]
      public bool IsAlive => Status is BusinessTaskStatus.Queued or BusinessTaskStatus.Running;
   }
}
