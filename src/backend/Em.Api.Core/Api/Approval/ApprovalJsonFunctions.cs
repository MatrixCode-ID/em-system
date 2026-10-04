namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Fungsi database yang hanya boleh dipakai di dalam query, dipetakan ke fungsi bawaan database.
   /// </summary>
   /// <remarks>
   /// Dipakai daftar request untuk mengurutkan berdasarkan kolom ringkasan milik modul, yang disimpan
   /// sebagai JSON. Hanya SQL Server yang dipetakan di sini - sama seperti transaksi lintas database
   /// approval, yang juga hanya berjalan di sana.
   /// </remarks>
   internal static class ApprovalJsonFunctions
   {
      /// <summary>Nilai skalar di dalam teks JSON, menurut path-nya. Tidak bisa dipanggil di luar query.</summary>
      /// <param name="json">Teks JSON-nya.</param>
      /// <param name="path">Path ke nilainya, mis. <c>$."Customer"</c>.</param>
      public static string? JsonValue(string? json, string path) =>
         throw new NotSupportedException("This function can only be used inside a query.");
   }
}
