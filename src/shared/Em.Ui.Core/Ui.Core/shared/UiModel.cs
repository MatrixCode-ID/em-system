using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Base class untuk semua model UI yang mewakili satu baris data dari sebuah tabel.
   /// Menyediakan notifikasi perubahan property, kolom standar yang dimiliki setiap tabel,
   /// penanda status (<see cref="IsBusy"/>, <see cref="IsDirty"/>) yang bisa langsung dipakai
   /// untuk binding, serta alur baca-tulis data yang seragam: menyimpan salinan data asli,
   /// membatalkan perubahan, menyimpan ke server, dan memuat ulang. Turunannya hanya perlu
   /// mengisi pemetaan kolom dan dua pemanggilan service.
   /// <para>
   /// Selain kolom, kelas ini juga mengurus isi <see cref="json_object"/> - termasuk
   /// <see cref="UiIcon"/>, yang tinggal di dalam JSON itu dan bukan kolom tersendiri.
   /// </para>
   /// </summary>
   /// <typeparam name="TEntity">Tipe data mentah dari tabel yang diwakili model ini.</typeparam>
   /// <typeparam name="TService">Interface service module yang menyediakan akses datanya.</typeparam>
   public abstract class UiModel<TEntity, TService> : INotifyPropertyChanged
      where TEntity : class, new()
      where TService : class, IServices
   {
      #region Standard Columns

      /// <summary>Waktu perubahan terakhir baris ini. Diisi ulang otomatis setiap kali disimpan.</summary>
      public DateTime ustamp {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Waktu baris ini pertama kali dibuat.</summary>
      public DateTime datestamp {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Data tambahan bebas dalam bentuk JSON, untuk kebutuhan yang tidak punya kolom sendiri.</summary>
      public string? json_object {
         get;
         set => SetField(ref field, value);
      }

      #endregion

      #region Json Object

      private JsonObject? _json;
      private string? _jsonSource;

      private JsonObject CurrentJson() {
         if (_json is not null && _jsonSource == json_object) return _json;

         _json = string.IsNullOrWhiteSpace(json_object)
            ? new JsonObject()
            : JsonNode.Parse(json_object) as JsonObject ?? new JsonObject();
         _jsonSource = json_object;
         return _json;
      }

      /// <summary>
      /// Membaca satu field dari <see cref="json_object"/>. Dipakai turunan di dalam
      /// <see cref="ReadFrom"/> untuk mengangkat field JSON menjadi property biasa, sehingga bisa
      /// di-binding dan ikut terlacak sebagai perubahan seperti kolom lainnya.
      /// </summary>
      /// <typeparam name="T">Tipe nilai yang diharapkan.</typeparam>
      /// <param name="fieldName">Nama field di dalam JSON.</param>
      /// <returns>Nilai field tersebut, atau nilai default jika fieldnya tidak ada.</returns>
      protected T? GetJson<T>(string fieldName) {
         return CurrentJson().TryGetPropertyValue(fieldName, out var node) && node is not null
            ? node.Deserialize<T>()
            : default;
      }

      // Menyusun ulang json_object lewat BuildJson tepat sebelum model dipetakan ke data mentah,
      // sekaligus menjadikan hasilnya cache JSON yang baru supaya GetJson berikutnya tidak mem-parse ulang.
      // Sengaja tidak menandai model sebagai berubah: isinya hanya turunan dari property yang sudah ada,
      // sehingga memanggil ToEntity() tidak boleh membuat model jadi dirty dengan sendirinya.
      private void RefreshJson() {
         // Ditulis sebelum BuildJson supaya turunan masih punya kesempatan terakhir mengubahnya,
         // dan dihapus saat belum dipilih supaya "belum memilih" tidak meninggalkan jejak di data.
         var patch = CurrentJson();
         if (UiIcon == UiIconType.Unspecified) patch.Remove("UiIcon");
         else patch["UiIcon"] = UiIcons.ToToken(UiIcon);

         var json = BuildJson(patch);
         var text = json.Count == 0 ? null : json.ToJsonString();

         var wasLoading = _isLoading;
         _isLoading = true;
         try {
            json_object = text;
         }
         finally {
            _isLoading = wasLoading;
         }

         _json = json;
         _jsonSource = text;
      }

      #endregion

      #region Ui Icon

      /// <summary>
      /// Ikon yang dipilih user untuk baris ini. Bukan kolom database - nilainya tinggal di dalam
      /// <see cref="json_object"/> sebagai field <c>UiIcon</c>, dan <see cref="UiIconType.Unspecified"/>
      /// berarti user memang belum memilih apa pun.
      /// </summary>
      public UiIconType UiIcon {
         get;
         set {
            if (SetField(ref field, value)) OnPropertyChanged(nameof(EffectiveUiIcon));
         }
      }

      /// <summary>
      /// Ikon bawaan entitas ini, dipakai selama user belum memilih sendiri. Turunan yang memang
      /// tampil dengan ikon meng-override-nya; yang tidak, tidak perlu - dan tidak ada apa pun yang
      /// tertulis ke data karenanya.
      /// <para>
      /// Bawaannya melekat pada entitas, bukan pada layar, supaya satu baris yang sama tidak tampil
      /// dengan ikon berbeda di dua layar yang berbeda.
      /// </para>
      /// </summary>
      protected virtual UiIconType DefaultUiIcon => UiIconType.Unspecified;

      /// <summary>
      /// Ikon yang benar-benar digambar: pilihan user kalau ada, kalau tidak bawaan entitasnya.
      /// Ini yang dipakai untuk binding, bukan <see cref="UiIcon"/>.
      /// </summary>
      public UiIconType EffectiveUiIcon => UiIcon == UiIconType.Unspecified ? DefaultUiIcon : UiIcon;

      // Dibaca dari node JSON-nya langsung, bukan lewat GetJson<string>: isi json_object bisa saja
      // datang dari impor, dari edit SQL manual, atau dari klien lain, dan Deserialize<string> akan
      // melempar begitu node-nya ternyata angka atau objek. Apa pun selain teks dianggap tidak ada.
      //
      // Jatuhnya ke Unspecified, bukan ke DefaultUiIcon: kalau bawaan entitas ditulis balik ke
      // property, dia akan ikut tersimpan pada save berikutnya - baris yang user-nya tidak pernah
      // memilih ikon jadi punya ikon tertulis, dan bawaan yang berubah di kemudian hari tidak lagi
      // berlaku untuk baris itu.
      private void ReadUiIcon() {
         var token = CurrentJson().TryGetPropertyValue("UiIcon", out var node)
                     && node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

         UiIcon = UiIcons.Parse(token, UiIconType.Unspecified);
      }

      #endregion

      #region State

      /// <summary>
      /// Menandakan ada perubahan yang belum disimpan. Otomatis menjadi <c>true</c> begitu ada
      /// property data yang berubah, dan kembali <c>false</c> setelah disimpan atau dibatalkan.
      /// </summary>
      public bool IsDirty {
         get;
         protected set {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Menjalankan pengisian property dari sumber data tanpa membuat model ditandai berubah,
      /// lalu mereset <see cref="IsDirty"/>. Dipakai saat memuat data maupun membatalkan perubahan.
      /// </summary>
      /// <param name="loadAction">Aksi yang mengisi property-property model.</param>
      protected void Load(Action loadAction) {
         _isLoading = true;
         try {
            loadAction();
         }
         finally {
            _isLoading = false;
         }

         IsDirty = false;
      }

      private bool _isLoading;

      /// <summary>
      /// Menandakan baris ini belum pernah tersimpan di server - masih hidup di memory saja, dan
      /// kunci yang dibawanya baru berupa penanda sampai <see cref="SaveAsync"/> menerbitkannya.
      /// <para>
      /// Dibaca layar, bukan cuma turunan: baris yang belum lahir tidak punya id untuk ditunjuk isi
      /// apa pun, sehingga layar perlu tahu bagian mana yang harus dikunci dan apa yang ditampilkan
      /// menggantikan kunci yang belum ada. Yang mengubahnya tetap hanya turunan.
      /// </para>
      /// </summary>
      public bool IsBlank {
         get;
         protected set => SetField(ref field, value);
      } = false;

      #endregion

      #region INotifyPropertyChanged Changed Handler

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;

      /// <summary>Memicu event <see cref="PropertyChanged"/> untuk property tertentu.</summary>
      /// <param name="propertyName">Nama property yang berubah. Otomatis terisi nama member pemanggil.</param>
      protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      }

      /// <summary>
      /// Mengubah nilai field backing sebuah property data, lalu memicu <see cref="PropertyChanged"/>
      /// dan menandai model sebagai berubah (<see cref="IsDirty"/>) - kecuali sedang berjalan di
      /// dalam <see cref="Load"/>. Property status seperti <see cref="IsBusy"/> sengaja tidak lewat sini.
      /// </summary>
      /// <typeparam name="T">Tipe nilai property.</typeparam>
      /// <param name="field">Referensi ke field backing yang akan diubah.</param>
      /// <param name="value">Nilai baru.</param>
      /// <param name="propertyName">Nama property, otomatis terisi nama member pemanggil.</param>
      /// <returns><c>true</c> jika nilainya benar-benar berubah; <c>false</c> jika sama seperti sebelumnya.</returns>
      protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
         if (EqualityComparer<T>.Default.Equals(field, value)) return false;
         field = value;
         OnPropertyChanged(propertyName);
         if (!_isLoading) IsDirty = true;
         return true;
      }

      #endregion

      #region Data Access

      private TService? _service;

      /// <summary>
      /// Salinan data terakhir yang diketahui sama dengan yang tersimpan di server. Menjadi acuan
      /// saat perubahan dibatalkan lewat <see cref="RollBack"/>.
      /// </summary>
      protected TEntity Original { get; private set; }

      /// <summary>
      /// Membuat model, mengikatnya ke objek aplikasi tempat ia hidup, lalu langsung mengisinya dari
      /// data mentah. Karena pengisian data terjadi di sini, tidak ada langkah inisialisasi terpisah
      /// yang bisa terlewat - model tidak pernah ada dalam keadaan setengah jadi.
      /// </summary>
      /// <param name="app">Objek aplikasi, sumber DI container dan waktu server.</param>
      /// <param name="entity">Data mentah yang menjadi isi awal model.</param>
      /// <remarks>
      /// Constructor ini memanggil <see cref="ReadFrom"/> yang virtual. Itu aman karena field
      /// initializer kelas turunan sudah dijalankan sebelum constructor base. Yang belum berjalan
      /// adalah body constructor turunan, jadi apa pun yang diisi di sana - misalnya referensi ke
      /// model induk - belum tersedia saat pemetaan kolom berlangsung. Karena itu
      /// <see cref="ReadFrom"/> hanya boleh membaca dari parameternya sendiri.
      /// </remarks>
      protected UiModel(IEmApp app, TEntity entity) {
         App = app;
         Original = entity;

         // ReSharper disable once VirtualMemberCallInConstructor
         Load(() => {
            ReadFrom(entity);
            ReadUiIcon();
         });
      }

      /// <summary>Objek aplikasi tempat model ini hidup, sumber DI container dan waktu server.</summary>
      public IEmApp App { get; }

      /// <summary>Service module yang dipakai model ini, diambil dari DI container saat pertama dibutuhkan.</summary>
      internal TService Service => _service ??= App.ServiceProvider.GetRequiredService<TService>();

      #endregion

      #region Contract

      /// <summary>Menyalin nilai property model saat ini ke <paramref name="target"/>.</summary>
      /// <param name="target">Data mentah yang akan diisi.</param>
      protected abstract void WriteTo(TEntity target);

      /// <summary>
      /// Mengisi property model dari <paramref name="source"/>. Hanya boleh membaca dari
      /// <paramref name="source"/>: method ini dijalankan dari constructor base, saat body
      /// constructor turunan belum berjalan, sehingga apa pun yang diisi di sana - termasuk
      /// referensi ke model induk - masih kosong di titik ini.
      /// </summary>
      /// <param name="source">Data mentah yang menjadi sumber nilai.</param>
      protected abstract void ReadFrom(TEntity source);

      /// <summary>
      /// Mengambil ulang baris data ini dari server. Turunan yang menentukan kolom kunci mana yang
      /// dipakai, karena hanya dia yang tahu nama kolom kuncinya.
      /// </summary>
      /// <returns>Data mentah terbaru, atau <c>null</c> jika barisnya sudah tidak ada.</returns>
      protected abstract Task<TEntity?> FetchAsync();

      /// <summary>Mengirim perubahan satu baris data ke server.</summary>
      /// <param name="entity">Data mentah hasil pemetaan dari model.</param>
      protected abstract Task UpdateAsync(TEntity entity);

      /// <summary>
      /// Menyusun isi <see cref="json_object"/> yang akan ikut tersimpan. Dipanggil otomatis tepat
      /// sebelum model dipetakan ke data mentah - termasuk di dalam <see cref="SaveAsync"/> - jadi
      /// turunan tidak perlu memanggilnya sendiri. <paramref name="patch"/> sudah berisi isi JSON yang
      /// sekarang, tinggal ditambah atau dihapus field-nya; field yang tidak dikenal model ini -
      /// misalnya yang ditulis versi lain atau module lain - ikut terbawa utuh selama tidak dihapus,
      /// jadi menyimpan data tidak pernah menghapus isi yang tidak dipahami model ini. Model yang tidak
      /// menyimpan apa pun di JSON cukup mengembalikan <paramref name="patch"/> apa adanya, dan yang
      /// ingin mengosongkan isinya mengembalikan <see cref="JsonObject"/> kosong.
      /// </summary>
      /// <param name="patch">Isi JSON yang sekarang, siap ditambah atau dikurangi field-nya.</param>
      /// <returns>Isi JSON yang akan disimpan. Kalau kosong, kolomnya disimpan sebagai <c>null</c>.</returns>
      /// <example>
      /// <code>
      /// protected override JsonObject BuildJson(JsonObject patch) {
      ///    patch["Theme"] = Theme;
      ///    patch.Remove("FontSize");
      ///    return patch;
      /// }
      /// </code>
      /// </example>
      protected abstract JsonObject BuildJson(JsonObject patch);

      #endregion

      #region Methods

      /// <summary>Membuat data mentah baru yang berisi nilai property model saat ini.</summary>
      /// <returns>Data mentah hasil pemetaan, siap dikirim ke server.</returns>
      public TEntity ToEntity() {
         var entity = new TEntity();
         RefreshJson();
         WriteTo(entity);
         return entity;
      }

      /// <summary>Membatalkan seluruh perubahan yang belum disimpan, kembali ke nilai data asli.</summary>
      public void RollBack() {
         Load(() => {
            ReadFrom(Original);
            ReadUiIcon();
         });
      }

      /// <summary>
      /// Menjadikan nilai property model saat ini sebagai data asli yang baru, tanpa mengirim apa pun
      /// ke server. Dipakai turunan ketika perubahannya sudah dipastikan tersimpan lewat jalur lain
      /// (mis. penyimpanan sekaligus banyak baris).
      /// </summary>
      protected void Commit() {
         WriteTo(Original);
         IsDirty = false;
      }

      /// <summary>
      /// Menyimpan perubahan ke server. <see cref="ustamp"/> diisi ulang dengan waktu server
      /// sebelum data dikirim, dan data asli diperbarui setelah pengirimannya berhasil.
      /// </summary>
      public async Task SaveAsync() {
         try {
            ustamp = await App.GetDateStampAsync();
            var entity = ToEntity();

            if (IsBlank) {
               await InsertAsync(entity);
               IsBlank = false;

               // The key and the stamps the insert filled in only exist on the entity, so the model
               // is refilled from it rather than left holding the placeholder it was created with.
               Load(() => {
                  ReadFrom(entity);
                  ReadUiIcon();
               });
            }
            else await UpdateAsync(entity);

            Original = entity;
            IsDirty = false;
         }
         catch {
            throw;
         }
      }
      
      protected virtual Task InsertAsync(TEntity entity) => Task.FromException(new NotImplementedException());

      /// <summary>
      /// Mengambil ulang data dari server, lalu membuang seluruh perubahan yang belum disimpan.
      /// Baris yang belum pernah tersimpan tidak punya apa pun untuk diambil, jadi untuk baris
      /// seperti itu method ini tidak melakukan apa-apa. Isian yang sudah diketik sengaja
      /// dibiarkan: yang diminta memuat ulang, bukan mengosongkan - untuk mengosongkannya
      /// pakai <see cref="RollBack"/>.
      /// </summary>
      /// <exception cref="InvalidOperationException">Jika barisnya sudah tidak ada di server.</exception>
      public async Task ResetAsync() {
         try {
            // A row that was never sent has nothing on the server to be re-read, and asking for it
            // by a key that does not exist yet would come back empty and read as a deleted row.
            // Nothing is thrown away either: losing what was typed would be a strange answer to a
            // request to refresh.
            if (IsBlank) return;

            Original = await FetchAsync()
                       ?? throw new InvalidOperationException(
                          $"{typeof(TEntity).Name} was not found on the server.");
            RollBack();
         }
         catch {
            throw;
         }
      }

      #endregion
   }
}