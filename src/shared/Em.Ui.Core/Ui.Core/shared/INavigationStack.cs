namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Jalur navigasi yang ditampilkan satu host: deretan entri yang bisa ditelusuri maju-mundur,
   /// ditambah satu home opsional yang berdiri di depan jalur itu.
   /// <para>
   /// Aturannya: home tidak termasuk <see cref="Entries"/> - posisinya di depan jalur - dan body-nya
   /// tidak pernah dilepas. Membuka layar baru membuang semua entri di depan posisi saat ini lalu
   /// menambahkan entri baru di ujung. Mundur dari entri pertama mendarat di home tanpa membuang
   /// jalur, jadi maju masih bisa kembali ke sana; <see cref="NavigateHome"/>-lah yang membersihkan
   /// seluruh jalur.
   /// </para>
   /// </summary>
   public interface INavigationStack
   {
      /// <summary>Entri-entri di jalur ini, urut dari yang pertama dibuka. Home tidak termasuk.</summary>
      IReadOnlyList<INavigationEntry> Entries { get; }

      /// <summary>
      /// Entri yang sedang tampil - bisa juga <see cref="Home"/> - atau <c>null</c> kalau belum ada
      /// yang pernah ditampilkan.
      /// </summary>
      INavigationEntry? Current { get; }

      /// <summary>Entri home, atau <c>null</c> untuk stack tanpa home.</summary>
      INavigationEntry? Home { get; }

      /// <summary>Maju satu langkah di jalur ini.</summary>
      /// <returns><c>false</c> kalau tidak ada entri di depan, atau perpindahannya ditolak.</returns>
      Task<bool> Forward();

      /// <summary>Mundur satu langkah di jalur ini; dari entri pertama mundurnya ke home.</summary>
      /// <returns><c>false</c> kalau tidak ada tempat untuk mundur, atau perpindahannya ditolak.</returns>
      Task<bool> Backward();

      /// <summary>
      /// Pulang ke home, lalu membersihkan dan melepas seluruh entri di jalur ini. Tidak melakukan
      /// apa-apa pada stack tanpa home.
      /// </summary>
      Task NavigateHome();

      /// <summary>
      /// Membuang dan melepas seluruh entri di depan posisi saat ini. Dijalankan sendiri setiap kali
      /// layar baru membuka cabang.
      /// </summary>
      Task ClearForwardStacks();

      /// <summary>Apakah ada entri berjudul <paramref name="title"/> di jalur ini.</summary>
      /// <param name="title">Judul entri yang dicari.</param>
      bool IsInStack(string title);
   }
}
