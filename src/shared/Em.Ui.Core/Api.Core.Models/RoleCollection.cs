using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Wadah satu halaman role berikut angka-angka yang menyertainya. Isinya ikut bergerak sendiri
   /// di layar saat role ditambah atau dihapus - daftarnya <see cref="ObservableCollection{T}"/>
   /// dan perubahannya diteruskan lewat <see cref="INotifyCollectionChanged"/>.
   /// <para>
   /// Penyaringan dan pencarian sengaja tidak ada di sini: keduanya urusan tampilan, dan
   /// <c>CollectionView</c> di view model sudah punya jalurnya sendiri untuk itu.
   /// </para>
   /// </summary>
   public class RoleCollection : IEnumerable<Role>, INotifyCollectionChanged
   {
      private readonly ObservableCollection<Role> _roles = [];

      private RoleCollection(IEmApp app) {
         App = app;
         _roles.CollectionChanged += (_, e) => CollectionChanged?.Invoke(this, e);
      }

      #region Statics

      /// <summary>
      /// Memuat satu halaman role berikut angka anggota dan angka hak setiap role di dalamnya.
      /// </summary>
      /// <param name="app">Objek aplikasi pemilik service data.</param>
      /// <param name="page">Halaman yang dimuat, dimulai dari 1.</param>
      /// <param name="pageSize">Banyaknya role per halaman.</param>
      public static async Task<RoleCollection> LoadAsync(IEmApp app, int page = 1, int pageSize = 50) {
         var result = new RoleCollection(app) {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize < 1 ? 50 : pageSize
         };
         await result.ReloadAsync();
         return result;
      }

      #endregion

      #region IEnumerable<T> Implementation

      public IEnumerator<Role> GetEnumerator() => _roles.GetEnumerator();

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

      #endregion

      #region INotifyCollectionChanged Implementation

      /// <inheritdoc />
      public event NotifyCollectionChangedEventHandler? CollectionChanged;

      #endregion

      #region Properties

      /// <summary>Objek aplikasi tempat wadah ini hidup, sumber DI container dan waktu server.</summary>
      public IEmApp App { get; }

      /// <summary>Banyaknya role di halaman yang sedang dipegang wadah ini.</summary>
      public int Count => _roles.Count;

      /// <summary>Banyaknya role seluruhnya di server, bukan hanya yang ada di halaman ini.</summary>
      public int TotalCount {
         get;
         private set;
      }

      /// <summary>Banyaknya penugasan role seluruhnya di server - berapa kali role dipegang seseorang.</summary>
      public int TotalAssignments {
         get;
         private set;
      }

      /// <summary>Halaman yang sedang dimuat, dimulai dari 1.</summary>
      public int Page {
         get;
         private set;
      } = 1;

      /// <summary>Banyaknya role per halaman.</summary>
      public int PageSize {
         get;
         private set;
      } = 50;

      public Role this[int index] => _roles[index];

      /// <summary>
      /// Role bernama <paramref name="name"/>. Nama role unik, jadi tidak ada kemungkinan dua
      /// jawaban. Nama yang tidak ada dianggap kesalahan pemanggil - pakai
      /// <see cref="Contains"/> lebih dulu kalau "tidak ketemu" memang jawaban yang wajar.
      /// </summary>
      /// <exception cref="InvalidOperationException">Kalau tidak ada role bernama itu di halaman ini.</exception>
      public Role this[string name] {
         get {
            var result = _roles.SingleOrDefault(r => r.cRoleName == name);
            if (result == null) {
               throw new InvalidOperationException(
                  $"There is no role named '{name}' in this collection. Check the spelling, or test with 'Contains' first.");
            }

            return result;
         }
      }

      /// <summary><c>true</c> kalau ada role bernama <paramref name="name"/> di halaman ini.</summary>
      public bool Contains(string name) => _roles.Any(r => r.cRoleName == name);

      #endregion

      #region Methods

      private ICredentialServices Service => App.ServiceProvider.GetRequiredService<ICredentialServices>();

      /// <summary>
      /// Memuat ulang halaman yang sedang dipegang, berikut seluruh angkanya.
      /// </summary>
      public async Task ReloadAsync() {
         var svc = Service;
         var rows = await svc.GetVi_Roles_InPage(Page, PageSize);

         _roles.Clear();
         foreach (var row in rows) {
            _roles.Add(Role.Build(App, row));
         }

         // Satu permintaan untuk angka seluruh daftar, bukan satu per role: halaman berisi puluhan
         // role, dan menghitungnya satu per satu berarti puluhan perjalanan ke server untuk dua
         // angka per baris.
         var counters = (await svc.GetMeta_RoleCounters()).ToDictionary(r => r.cRoleId);
         foreach (var role in _roles) {
            if (!counters.TryGetValue(role.cRoleId, out var counter)) continue;
            role.MemberCount = counter.MemberCount;
            role.ClaimCount = counter.ClaimCount;
         }

         TotalCount = await svc.GetTa_Roles_Count();
         TotalAssignments = await svc.GetTa_UserRoles_Count();
      }

      /// <summary>
      /// Pindah ke halaman lain lalu memuatnya.
      /// </summary>
      /// <param name="page">Halaman yang dituju, dimulai dari 1.</param>
      public Task GoToPageAsync(int page) {
         Page = page < 1 ? 1 : page;
         return ReloadAsync();
      }

      /// <summary>
      /// Membuat role baru di server lalu memasukkannya ke daftar ini, sehingga layar yang terikat
      /// padanya langsung melihatnya tanpa perlu memuat ulang seluruh halaman.
      /// </summary>
      /// <param name="name">Nama role baru. Nama role unik di seluruh server.</param>
      /// <param name="description">Keterangan singkat isinya, boleh <c>null</c>.</param>
      public async Task<Role> NewRoleAsync(string name, string? description) {
         var role = Role.CreateNewRole(App);
         role.cRoleName = name;
         role.cRoleDescription = description;
         await role.SaveAsync();

         _roles.Add(role);
         TotalCount++;
         return role;
      }

      /// <summary>
      /// Memasukkan role yang objeknya sudah dibuat pemanggil - biasanya baris draft yang sudah
      /// hidup di layar sebelum ada apa pun di server. Baris yang belum pernah tersimpan disimpan
      /// dulu di sini, sehingga pemanggil tidak perlu mengingat urutan "simpan dulu, baru masukkan".
      /// <para>
      /// Sepasang dengan <see cref="NewRoleAsync(string,string?)"/>, bukan penggantinya: yang itu
      /// untuk alur yang namanya sudah diketahui di muka, yang ini untuk alur yang objeknya sudah
      /// ada lebih dulu.
      /// </para>
      /// </summary>
      /// <param name="role">Role yang dimasukkan; boleh sudah tersimpan, boleh masih draft.</param>
      /// <returns>Role yang sama, sudah tersimpan dan sudah ada di daftar ini.</returns>
      public async Task<Role> NewRoleAsync(Role role) {
         ArgumentNullException.ThrowIfNull(role);

         if (role.IsBlank) await role.SaveAsync();

         // Diperiksa dulu, bukan ditambahkan begitu saja: memanggil ini dua kali atas objek yang
         // sama - mis. karena simpan yang pertama gagal di langkah lain lalu diulang - tidak boleh
         // menghasilkan dua baris yang sebenarnya satu role.
         if (!_roles.Contains(role)) {
            _roles.Add(role);
            TotalCount++;
         }

         return role;
      }

      /// <summary>
      /// Menghapus satu role sungguhan lalu mengeluarkannya dari daftar ini. Hak dan penugasannya
      /// ikut hilang - lihat <see cref="Role.DeleteAsync"/>.
      /// </summary>
      public async Task RemoveAsync(Role role) {
         await role.DeleteAsync();

         // Angka penugasan ikut berkurang sebanyak anggota role yang barusan hilang, dan angka itu
         // sudah ada di tangan - membaca ulang seluruh halaman hanya untuk dua penghitung toolbar
         // adalah harga yang tidak perlu dibayar.
         TotalAssignments -= role.MemberCount;
         if (_roles.Remove(role)) TotalCount--;
      }

      #endregion
   }
}
