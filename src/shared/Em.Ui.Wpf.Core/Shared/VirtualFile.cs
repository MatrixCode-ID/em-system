using System.IO;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Satu file atau folder yang bisa diseret keluar aplikasi - ke Windows Explorer atau aplikasi lain
   /// yang menerima file - tanpa pernah ada sebagai file lokal. Penerimanya yang meminta isinya lewat
   /// <see cref="OpenRead"/> dan menulisnya langsung ke tempat tujuan; tidak ada salinan sementara di
   /// disk.
   /// </summary>
   public sealed class VirtualFile
   {
      /// <summary>
      /// Nama relatif terhadap tempat jatuhan, dengan <c>\</c> sebagai pemisah folder (mis.
      /// <c>Release 1.0\setup.msi</c>). Folder induknya harus ikut didaftarkan lebih dulu sebagai
      /// <see cref="VirtualFile"/> ber-<see cref="IsFolder"/> <c>true</c>.
      /// </summary>
      public required string RelativePath { get; init; }

      /// <summary><c>true</c> untuk folder, yang tidak punya isi.</summary>
      public bool IsFolder { get; init; }

      /// <summary>Ukuran isi dalam byte; dipakai penerima untuk menampilkan kemajuan.</summary>
      public long Size { get; init; }

      /// <summary>Waktu terakhir diubah, yang ikut ditulis ke file tujuan.</summary>
      public DateTime LastWriteTimeUtc { get; init; }

      /// <summary>
      /// Membuka isinya untuk dibaca mulai dari posisi byte yang diminta sampai akhir. Baru dipanggil
      /// saat penerima benar-benar meminta isinya, bisa lebih dari sekali kalau penerima melompat ke
      /// posisi lain, dan dari thread mana pun - bukan thread UI - jadi boleh memblokir, misalnya
      /// selama mengunduh. <c>null</c> untuk folder.
      /// </summary>
      public Func<long, Stream>? OpenRead { get; init; }
   }

   /// <summary>
   /// Muatan <see cref="DragSource"/> yang juga bisa dijatuhkan di luar aplikasi sebagai file. Muatan
   /// seperti ini tetap diterima <see cref="DropTarget"/> di dalam aplikasi apa adanya; di luar
   /// aplikasi, penerimanya mendapat file-file dari <see cref="ResolveVirtualFiles"/>.
   /// </summary>
   public interface IVirtualFileSource
   {
      /// <summary>
      /// Menyusun daftar file dan folder yang diserahkan ke penerima di luar aplikasi. Baru dipanggil
      /// saat penerima memintanya, di thread UI, dan paling banyak sekali per drag.
      /// </summary>
      IReadOnlyList<VirtualFile> ResolveVirtualFiles();
   }
}
