namespace Em.Shared
{
   /// <summary>
   /// Menentukan warna sebuah baris data dari namanya. Warnanya tidak disimpan di mana pun: nama
   /// entitas dihitung dengan <see cref="Crc32"/> lalu sisa baginya menunjuk salah satu slot warna
   /// di palette. Nama yang sama karena itu selalu mendapat warna yang sama - hari ini, besok, dan
   /// di klien lain sekali pun, selama normalisasi dan algoritmanya sama. Itu sebabnya keduanya
   /// tinggal di sini, bukan di lapisan tampilan masing-masing klien.
   /// <para>
   /// Konsekuensi yang disadari: mengganti nama sebuah baris akan memindahkan warnanya.
   /// </para>
   /// </summary>
   public static class UiTint
   {
      /// <summary>
      /// Memilih slot warna untuk sebuah nama. Huruf besar-kecil dan spasi di ujung nama tidak
      /// berpengaruh - <c>" Admin "</c> dan <c>"admin"</c> mendapat slot yang sama.
      /// </summary>
      /// <param name="name">Nama entitas yang diwarnai; kosong atau <c>null</c> selalu slot 0.</param>
      /// <param name="slotCount">
      /// Banyaknya warna yang tersedia di palette. Diserahkan pemanggil karena jumlah warna adalah
      /// keputusan lapisan tampilan dan boleh bertambah kapan saja - karena warnanya tidak disimpan,
      /// menambah warna hanya mengubah tampilan, tidak ada data yang jadi salah.
      /// </param>
      /// <returns>Nomor slot antara 0 sampai <paramref name="slotCount"/> - 1.</returns>
      /// <exception cref="ArgumentOutOfRangeException">Jika <paramref name="slotCount"/> kurang dari 1.</exception>
      public static int SlotOf(string? name, int slotCount) {
         ArgumentOutOfRangeException.ThrowIfLessThan(slotCount, 1);

         if (string.IsNullOrWhiteSpace(name)) return 0;

         var normalized = name.Trim().ToLowerInvariant();
         return (int)(Crc32.Compute(normalized) % (uint)slotCount);
      }
   }
}
