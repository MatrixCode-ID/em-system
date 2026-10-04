using System.Globalization;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Satu pilihan "jumlah baris per halaman" pada daftar berhalaman, berisi angkanya
   /// (<see cref="Size"/>) sekaligus tulisan yang muncul di combo box (<see cref="Caption"/>).
   /// Dipakai supaya pilihan "ALL" bisa ikut dalam daftar yang sama tanpa perlu tipe terpisah.
   /// </summary>
   public sealed class PageSizeOption
   {
      /// <summary>
      /// Nilai <see cref="Size"/> yang berarti "tampilkan semua baris dalam satu halaman".
      /// Sengaja dibuat sangat besar (bukan 0 atau negatif) supaya perhitungan halaman biasa
      /// tetap benar tanpa aturan khusus: jumlah halamannya otomatis menjadi satu.
      /// </summary>
      public const int AllRows = int.MaxValue;

      /// <summary>
      /// Pilihan siap pakai untuk "semua baris".
      /// </summary>
      public static PageSizeOption All { get; } = new(AllRows, "ALL");

      /// <summary>
      /// Membuat pilihan dengan jumlah baris tertentu, dengan tulisan berupa angkanya sendiri.
      /// </summary>
      /// <param name="size">Jumlah baris per halaman, harus lebih besar dari nol.</param>
      public static PageSizeOption Of(int size) =>
         new(size, size.ToString(CultureInfo.InvariantCulture));

      private PageSizeOption(int size, string caption) {
         Size = size;
         Caption = caption;
      }

      /// <summary>
      /// Jumlah baris per halaman yang diwakili pilihan ini.
      /// </summary>
      public int Size { get; }

      /// <summary>
      /// Tulisan yang ditampilkan di UI untuk pilihan ini.
      /// </summary>
      public string Caption { get; }

      /// <summary>
      /// Menandakan pilihan ini adalah "semua baris" dan bukan angka tertentu.
      /// </summary>
      public bool IsAll => Size == AllRows;

      /// <inheritdoc />
      public override string ToString() => Caption;
   }
}
