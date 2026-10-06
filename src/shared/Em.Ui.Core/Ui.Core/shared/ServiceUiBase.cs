using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Base class marker untuk service module di sisi UI, analog dengan <c>ServicesBase</c> di backend
   /// tapi untuk hierarki UI (bukan dispatcher HTTP). Class service module frontend yang mengimplementasikan
   /// <see cref="IServices"/> disarankan menurunkan class ini.
   /// </summary>
   public abstract class ServiceUiBase : IServices
   {
      public ServiceUiBase(IEmApp app) {
         App = app;
         // GetType() mengembalikan tipe turunan yang konkret (mis. SampleServices), bukan ServiceUiBase,
         // dan constructor-nya sudah cukup untuk itu - tidak ada alasan menunda pembacaannya, dan
         // penundaan itulah yang dulu membuat claim ditanyakan dengan ModuleName masih kosong (module
         // service ditanyakan lewat XxxCommandAllowed sebelum request pertama pernah berjalan).
         // Tidak wajib: service UI tanpa [Module] sudah berjalan begitu selama ini dan jatuh ke nama
         // class.
         ModuleName = ModuleAttribute.ResolveName(GetType(), required: false);
      }

      public string ModuleName { get; }
      public ApiClient? ApiClient { get; protected set; }

      public virtual IEmApp App { get; }

      protected Task<T> GetAsync<T>(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.GetAsync<T>(ModuleName, action, args);
      }

      /// <summary>
      /// Memanggil action GET milik module ini yang mengembalikan isi file. Stream hasilnya wajib ditutup
      /// pemanggil; lihat <see cref="Shared.ApiClient.GetStreamAsync(string, string, object[])"/> untuk
      /// aturan lengkapnya.
      /// </summary>
      /// <param name="action">Nama action, biasanya <c>nameof</c> method yang sedang diimplementasikan.</param>
      /// <param name="args">Argumen action.</param>
      protected Task<Stream> GetStreamAsync(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.GetStreamAsync(ModuleName, action, args);
      }

     protected Task<T> PostAsync<T>(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync<T>(ModuleName, action, args);
      }

      protected Task PostAsync(string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync(ModuleName, action, args);
      }

      /// <summary>
      /// Calls a POST action of this module with its own time limit; see
      /// <see cref="Shared.ApiClient.PostAsync{T}(TimeSpan, string, string, object[])"/>.
      /// </summary>
      protected Task<T> PostAsync<T>(TimeSpan timeout, string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync<T>(timeout, ModuleName, action, args);
      }

      /// <inheritdoc cref="PostAsync{T}(TimeSpan, string, object[])"/>
      protected Task PostAsync(TimeSpan timeout, string action, params object[] args) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostAsync(timeout, ModuleName, action, args);
      }

      /// <summary>
      /// Memanggil action ber-stream milik module ini: isi <paramref name="content"/> dikirim mentah, dan
      /// <paramref name="payload"/> - objek yang diterima parameter lain action itu - ikut di header.
      /// Tanpa batas waktu; lihat <see cref="Shared.ApiClient.PostStreamAsync(string, string, Stream, object?)"/>
      /// untuk aturan lengkapnya.
      /// </summary>
      /// <param name="action">Nama action, biasanya <c>nameof</c> method yang sedang diimplementasikan.</param>
      /// <param name="content">Stream yang dikirim; tidak ditutup oleh method ini.</param>
      /// <param name="payload">Objek untuk parameter selain stream, atau <c>null</c> kalau tidak ada.</param>
      protected Task PostStreamAsync(string action, Stream content, object? payload = null) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostStreamAsync(ModuleName, action, content, payload);
      }

      /// <summary>
      /// Sama dengan <see cref="PostStreamAsync(string, Stream, object?)"/>, hanya saja jawaban server
      /// dibaca menjadi <typeparamref name="T"/>.
      /// </summary>
      /// <inheritdoc cref="PostStreamAsync(string, Stream, object?)"/>
      protected Task<T> PostStreamAsync<T>(string action, Stream content, object? payload = null) {
         if (ApiClient == null)
            throw new InvalidOperationException("Api Client is not ready");
         return ApiClient!.PostStreamAsync<T>(ModuleName, action, content, payload);
      }
   }
}