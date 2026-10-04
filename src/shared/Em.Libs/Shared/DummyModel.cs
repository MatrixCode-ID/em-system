namespace Em.Shared
{
   /// <summary>
   /// Model contoh (placeholder) yang dipakai untuk keperluan testing/demo binding POST,
   /// misalnya pada action <c>PostGetDataAsync</c> di module <c>Em.Sample</c>.
   /// Bukan model bisnis sungguhan — jangan dipakai sebagai referensi struktur data produksi.
   /// </summary>
   public class DummyModel
   {
      /// <summary>
      /// Nama contoh.
      /// </summary>
      public string Name { get; set; } = "";

      /// <summary>
      /// Nilai numerik contoh.
      /// </summary>
      public int Value { get; set; }

      /// <summary>
      /// Identifier contoh berbentuk <see cref="System.Guid"/>, opsional.
      /// </summary>
      public Guid? Guid { get; set; }
   }
}
