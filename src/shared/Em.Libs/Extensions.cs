/// <summary>
/// Kumpulan extension method umum yang bisa dipakai di seluruh solution (frontend maupun backend).
/// </summary>
// ReSharper disable once CheckNamespace
public static class Extensions
{
   /// <summary>
   /// Menjalankan <paramref name="action"/> untuk setiap elemen pada <paramref name="source"/>,
   /// setara dengan <c>foreach</c> tapi dalam bentuk method chain.
   /// </summary>
   /// <typeparam name="T">Tipe elemen pada koleksi.</typeparam>
   /// <param name="source">Koleksi yang akan di-iterasi.</param>
   /// <param name="action">Aksi yang dijalankan untuk tiap elemen. Jika <c>null</c>, elemen tersebut dilewati.</param>
   public static void EachOf<T>(this IEnumerable<T> source, Action<T> action) {
      foreach (var item in source) {
         action?.Invoke(item);
      }
   }
}
