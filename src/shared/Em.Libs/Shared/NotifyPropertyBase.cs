using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Em.Shared
{
   /// <summary>
   /// Base class untuk ViewModel/model yang butuh notifikasi perubahan property (<see cref="INotifyPropertyChanged"/>),
   /// dengan penyimpanan nilai property secara dictionary (tanpa perlu field backing manual per property).
   /// Dipakai sebagai fondasi MVVM di sisi WPF, tapi ditaruh di <c>Em.Libs</c> (bukan proyek WPF) supaya
   /// bisa dipakai ulang oleh model non-UI yang tetap butuh notifikasi perubahan.
   /// </summary>
   public abstract class NotifyPropertyBase : INotifyPropertyChanged
   {
      private readonly Dictionary<string, object?> _propertyValues = [];
      private readonly Dictionary<string, Action<object?>> _propertyChangedActions = [];

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;

      /// <summary>
      /// Memicu event <see cref="PropertyChanged"/> untuk nama property tertentu.
      /// </summary>
      /// <param name="propertyName">
      /// Nama property yang berubah. Otomatis terisi nama member pemanggil jika tidak diisi eksplisit.
      /// </param>
      protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "") {
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      }


      /// <summary>
      /// Mengubah nilai field backing biasa (bukan lewat dictionary internal), lalu memicu
      /// <see cref="PropertyChanged"/> dan callback <paramref name="onChanged"/> jika nilainya berbeda.
      /// Cocok dipakai untuk property yang sudah punya field backing eksplisit.
      /// </summary>
      /// <typeparam name="T">Tipe nilai property.</typeparam>
      /// <param name="field">Referensi ke field backing yang akan diubah.</param>
      /// <param name="value">Nilai baru.</param>
      /// <param name="onChanged">Callback opsional yang dijalankan setelah nilai berubah.</param>
      /// <param name="propertyName">Nama property, otomatis terisi lewat <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns><c>true</c> jika nilai benar-benar berubah dan disimpan; <c>false</c> jika nilai sama seperti sebelumnya.</returns>
      protected bool SetField<T>(
         ref T field,
         T value,
         Action<T>? onChanged = null,
         [CallerMemberName] string propertyName = "") {
         if (EqualityComparer<T>.Default.Equals(field, value)) return false;

         field = value;
         OnPropertyChanged(propertyName);
         onChanged?.Invoke(value);
         return true;
      }

      /// <summary>
      /// Mengambil nilai property dari penyimpanan internal (dictionary), tanpa perlu field backing.
      /// Jika property belum pernah di-set, nilainya diinisialisasi dengan <paramref name="defaultValue"/>.
      /// </summary>
      /// <typeparam name="T">Tipe nilai property.</typeparam>
      /// <param name="defaultValue">Nilai default jika property belum pernah di-set.</param>
      /// <param name="propertyName">Nama property, otomatis terisi lewat <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns>Nilai property saat ini.</returns>
      protected T Get<T>(T defaultValue = default!, [CallerMemberName] string propertyName = "") {
         EnsurePropertyExists(propertyName);

         if (_propertyValues.TryGetValue(propertyName, out var value))
            return (T)value!;

         _propertyValues[propertyName] = defaultValue;
         return defaultValue;
      }

      /// <summary>
      /// Menyimpan nilai property ke penyimpanan internal (dictionary), lalu memicu
      /// <see cref="PropertyChanged"/>, callback <paramref name="onChanged"/>, dan callback yang
      /// terdaftar lewat <see cref="RegisterValueChanged{T}"/> untuk property ini — jika nilainya berubah.
      /// </summary>
      /// <typeparam name="T">Tipe nilai property.</typeparam>
      /// <param name="value">Nilai baru yang akan disimpan.</param>
      /// <param name="onChanged">Callback opsional yang dijalankan setelah nilai berubah.</param>
      /// <param name="propertyName">Nama property, otomatis terisi lewat <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns><c>true</c> jika nilai benar-benar berubah dan disimpan; <c>false</c> jika nilai sama seperti sebelumnya.</returns>
      protected bool Set<T>(
         T value,
         Action<T>? onChanged = null,
         [CallerMemberName] string propertyName = "") {
         EnsurePropertyExists(propertyName);

         if (_propertyValues.TryGetValue(propertyName, out var currentValue) &&
             EqualityComparer<T>.Default.Equals((T)currentValue!, value)) {
            return false;
         }

         _propertyValues[propertyName] = value;
         OnPropertyChanged(propertyName);
         onChanged?.Invoke(value);

         if (_propertyChangedActions.TryGetValue(propertyName, out var registeredAction))
            registeredAction.Invoke(value);

         return true;
      }

      /// <summary>
      /// Mendaftarkan callback tambahan yang akan dipanggil setiap kali nilai property
      /// <paramref name="propertyName"/> berubah lewat <see cref="Set{T}"/>. Berguna untuk
      /// reaksi lintas-property tanpa harus meng-override <see cref="OnPropertyChanged"/>.
      /// </summary>
      /// <typeparam name="T">Tipe nilai property yang diamati.</typeparam>
      /// <param name="propertyName">Nama property yang diamati.</param>
      /// <param name="action">Aksi yang dijalankan tiap kali nilai property berubah.</param>
      protected void RegisterValueChanged<T>(string propertyName, Action<T> action) {
         ArgumentNullException.ThrowIfNull(action);
         EnsurePropertyExists(propertyName);

         _propertyChangedActions[propertyName] = value => action((T)value!);
      }

      /// <summary>
      /// Memastikan <paramref name="propertyName"/> memang merupakan property yang ada pada tipe ini,
      /// untuk mencegah typo nama property (mis. salah ketik lewat <c>nameof</c> atau caller member name).
      /// </summary>
      /// <param name="propertyName">Nama property yang divalidasi.</param>
      /// <returns><see cref="PropertyInfo"/> dari property tersebut.</returns>
      /// <exception cref="ArgumentException">Jika <paramref name="propertyName"/> kosong.</exception>
      /// <exception cref="NotSupportedException">Jika tidak ditemukan property dengan nama tersebut pada tipe ini.</exception>
      private PropertyInfo EnsurePropertyExists(string propertyName) {
         if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));

         return GetType().GetProperty(propertyName) ??
                throw new NotSupportedException($"Member \"{propertyName}\" is not supported or is not a property.");
      }

      /// <summary>
      /// Memicu event <see cref="PropertyChanged"/> untuk <paramref name="propertyName"/> dan
      /// mengembalikan argumen event yang dipakai, untuk kasus yang butuh akses ke <see cref="PropertyChangedEventArgs"/>-nya.
      /// </summary>
      /// <param name="propertyName">Nama property, otomatis terisi lewat <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns>Argumen event yang dipakai untuk memicu <see cref="PropertyChanged"/>.</returns>
      protected PropertyChangedEventArgs NotifyChanged([CallerMemberName] string propertyName = "") {
         var arg = new PropertyChangedEventArgs(propertyName);
         PropertyChanged?.Invoke(this, arg);
         return arg;
      }

      /// <summary>
      /// Menandakan apakah objek sedang dalam kondisi sibuk (mis. proses async berjalan),
      /// biasanya dipakai untuk mengaktifkan/nonaktifkan UI (loading indicator, disable tombol, dsb.).
      /// </summary>
      public bool IsBusy {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(IsNotBusy)));
      }

      /// <summary>
      /// Kebalikan dari <see cref="IsBusy"/>, disediakan agar bisa langsung dipakai untuk binding
      /// (mis. <c>IsEnabled</c>) tanpa converter tambahan di XAML.
      /// </summary>
      public bool IsNotBusy => !IsBusy;

      /// <summary>
      /// Menandakan apakah objek sedang dalam kondisi menunggu (mis. menunggu response server).
      /// </summary>
      public bool InWaiting {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(IsNotWaiting)));
      }

      /// <summary>
      /// Kebalikan dari <see cref="InWaiting"/>, disediakan agar bisa langsung dipakai untuk binding
      /// tanpa converter tambahan di XAML.
      /// </summary>
      public bool IsNotWaiting => !InWaiting;

      /// <summary>
      /// Keterangan singkat tentang apa yang sedang ditunggu, mis. "Saving user...". Ditampilkan
      /// oleh lapisan tunggu selama <see cref="InWaiting"/> bernilai <c>true</c>; kalau dibiarkan
      /// kosong, lapisan itu hanya menampilkan judul bakunya saja.
      /// </summary>
      public string WaiterText {
         get => Get<string>();
         set => Set(value);
      }
   }
}
