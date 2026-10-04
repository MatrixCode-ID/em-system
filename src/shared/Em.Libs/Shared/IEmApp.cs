namespace Em.Shared
{
   /// <summary>
   /// Kontrak minimal untuk objek aplikasi (<c>EmApp</c>) yang dibagikan lewat <c>Em.Models</c>
   /// ke module-module, tanpa module perlu mereferensikan implementasi <c>Em.Api.Core</c> secara langsung.
   /// </summary>
   public interface IEmApp
   {
      /// <summary>
      /// Pintu masuk untuk mengambil service dari DI container. Isinya hanya bisa dibaca:
      /// pendaftaran service seluruhnya dilakukan lewat callback <c>EmAppBuilder</c> saat
      /// <c>BuildApp</c>, dan setelah aplikasi berjalan container-nya terkunci - tidak ada lagi
      /// service yang boleh ditambahkan.
      /// </summary>
      /// <remarks>
      /// Yang dikembalikan adalah provider yang berlaku pada saat properti ini dibaca, bukan selalu
      /// provider akar. Di sisi API, selama sebuah request diproses, yang dipakai adalah provider
      /// milik request tersebut, sehingga service ber-lifetime scoped (mis. koneksi/context database)
      /// ikut siklus hidup request itu dan dibuang begitu request selesai. Di luar request - dan di
      /// sisi UI - yang dipakai adalah provider akar.
      /// <para>
      /// Karena itu hasil resolusi jangan disimpan melewati batas request: objeknya sudah dibuang
      /// saat request berakhir. Untuk pekerjaan latar yang berjalan di luar request, buat scope
      /// sendiri lewat <c>ServiceProvider.CreateScope()</c>.
      /// </para>
      /// </remarks>
      IServiceProvider ServiceProvider { get; }

      Task<DateTime> GetDateStampAsync();
   }
}
