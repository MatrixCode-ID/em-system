using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Model UI untuk satu role - sekumpulan hak yang bisa dipegang banyak user sekaligus, dengan
   /// masa berlaku per penugasan. Kolom role-nya sendiri disimpan lewat jalur biasa
   /// <see cref="UiModel{TEntity,TService}"/>; isi role - hak dan anggotanya - tinggal di baris
   /// tersendiri dan karena itu punya method sendiri yang langsung berbicara ke server.
   /// </summary>
   public class Role : UiModel<vi_Role, ICredentialServices>
   {
      #region Statics

      /// <summary>
      /// Membuat role kosong yang belum pernah tersimpan. Barisnya baru benar-benar lahir saat
      /// <see cref="UiModel{TEntity,TService}.SaveAsync"/> dipanggil - id-nya diterbitkan di sana.
      /// </summary>
      public static Role CreateNewRole(IEmApp app) {
         // Both starting values go on the raw row rather than on the model, because that row also
         // becomes what RollBack returns to: anything set on the model afterwards would be wiped by
         // the first Discard, and would mark the record changed before anything had been typed.
         var role = new Role(app, new vi_Role {
            cRoleId = "Save Role To Generate ID's",
            cRoleState = RoleState.Active
         }) {
            IsBlank = true
         };
         return role;
      }

      public static async Task<Role> GetRole_ByIdAsync(IEmApp app, string cRoleId) {
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var data = await svc.GetVi_Role_ById(cRoleId);
         return data == null
            ? throw new InvalidOperationException("Role not found")
            : Build(app, data);
      }

      public static async Task<Role[]> GetRoles_InPageAsync(IEmApp app, int page, int pageSize) {
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var rows = await svc.GetVi_Roles_InPage(page, pageSize);
         return [.. rows.Select(r => Build(app, r))];
      }

      public static Task<int> GetRoles_PageCountAsync(IEmApp app) =>
         app.ServiceProvider.GetRequiredService<ICredentialServices>().GetTa_Roles_Count();

      public static Role Build(IEmApp app, vi_Role data) => new(app, data);

      #endregion

      private Role(IEmApp app, vi_Role entity) : base(app, entity) { }

      #region Properties

      public string cRoleId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      public string cRoleName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public RoleState cRoleState {
         get;
         set => SetField(ref field, value);
      }

      public string? cRoleDescription {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>
      /// Banyaknya user yang memegang role ini. Diisi dari luar oleh <see cref="RoleCollection"/>
      /// yang membaca angkanya untuk seluruh daftar sekaligus - bukan dihitung sendiri per role,
      /// karena satu daftar berisi belasan role dan itu akan jadi belasan permintaan ke server.
      /// </summary>
      /// <remarks>
      /// Sengaja tidak lewat <c>SetField</c>: angka ini bukan kolom baris ini - ia dihitung dari
      /// baris penugasan di tabel lain - jadi mengisinya tidak boleh menandai role-nya berubah.
      /// Kalau iya, setiap role yang baru saja dibaca akan langsung terbaca sebagai role yang punya
      /// perubahan belum tersimpan, hanya karena daftarnya mengisikan angkanya.
      /// </remarks>
      public int MemberCount {
         get;
         set {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Banyaknya hak yang dibawa role ini. Diisi dari luar dengan alasan yang sama seperti
      /// <see cref="MemberCount"/>.
      /// </summary>
      /// <inheritdoc cref="MemberCount" />
      public int ClaimCount {
         get;
         set {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
         }
      }

      #endregion

      #region UiModel Implementation

      protected override void ReadFrom(vi_Role source) {
         cRoleId = source.cRoleId;
         cRoleName = source.cRoleName;
         cRoleState = source.cRoleState;
         cRoleDescription = source.cRoleDescription;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      protected override void WriteTo(vi_Role target) {
         target.cRoleId = cRoleId;
         target.cRoleName = cRoleName;
         target.cRoleState = cRoleState;
         target.cRoleDescription = cRoleDescription;
         target.ustamp = ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      protected override Task<vi_Role?> FetchAsync() => Service.GetVi_Role_ById(cRoleId);

      protected override Task UpdateAsync(vi_Role entity) => Service.PostTa_Role_Update(entity);

      protected override Task InsertAsync(vi_Role entity) {
         entity.cRoleId = $"{Ulid.NewUlid()}";

         // SaveAsync has already stamped the row with the server time, so the moment it was created
         // is that same moment - asking the server a second time would only cost a round trip and
         // leave the two stamps a few milliseconds apart.
         entity.datestamp = entity.ustamp;
         return Service.PostTa_Role_New(entity);
      }

      protected override JsonObject BuildJson(JsonObject patch) => patch;

      /// <inheritdoc />
      protected override UiIconType DefaultUiIcon => UiIconType.Shield;

      #endregion

      #region Claims

      /// <summary>Hak yang dibawa role ini, dibaca langsung dari server.</summary>
      public async Task<ClaimAction[]> GetRoleClaims() {
         var rows = await Service.GetTa_RoleClaims_ByRoleId(cRoleId);
         return [.. rows.Select(r => ClaimAction.FromKey(r.cClaimName))];
      }

      /// <summary>
      /// Menambahkan satu hak ke role ini. Hak yang sudah dipegang dilewati diam-diam oleh server,
      /// jadi memanggilnya dua kali bukan kesalahan.
      /// </summary>
      public Task AddClaim(ClaimAction action) {
         EnsureSaved();
         return Service.PostTa_RoleClaim_New(new ta_RoleClaim {
            cRoleId = cRoleId,
            cClaimName = action.Key
         });
      }

      /// <summary>Mencabut satu hak dari role ini.</summary>
      public Task RemoveClaim(ClaimAction action) {
         EnsureSaved();
         return Service.PostTa_RoleClaim_Delete(new ta_RoleClaim {
            cRoleId = cRoleId,
            cClaimName = action.Key
         });
      }

      #endregion

      #region Members

      /// <summary>
      /// User yang memegang role ini. Masa berlaku penugasannya tidak ikut di sini - baca
      /// <see cref="GetAssignments"/> untuk itu, dan jodohkan keduanya lewat <c>cUserId</c>.
      /// </summary>
      public async Task<User[]> GetMembers() {
         var rows = await Service.GetVi_Users_ByRoleId(cRoleId);
         return [.. rows.Select(r => User.Build(App, r))];
      }

      /// <summary>
      /// Penugasan role ini berikut masa berlakunya, satu baris per user. Sepasang dengan
      /// <see cref="GetMembers"/>: yang satu menjawab siapa, yang satu menjawab sejak dan sampai
      /// kapan.
      /// </summary>
      public Task<ta_UserRole[]> GetAssignments() => Service.GetTa_UserRoles_ByRoleId(cRoleId);

      /// <summary>
      /// Memberikan role ini kepada seorang user.
      /// </summary>
      /// <param name="member">User yang diberi role ini.</param>
      /// <param name="start">Awal masa berlakunya; <c>null</c> berarti berlaku sejak sekarang.</param>
      /// <param name="expiry">Akhir masa berlakunya; <c>null</c> berarti tanpa akhir.</param>
      public async Task AddMember(User member, DateTime? start = null, DateTime? expiry = null) {
         EnsureSaved();
         EnsureNotSystemAccount(member);

         var stamp = await App.GetDateStampAsync();
         await Service.PostTa_UserRole_New(new ta_UserRole {
            cUserId = member.cUserId,
            cRoleId = cRoleId,
            cUserRoleStart = start,
            cUserRoleExpiry = expiry,
            ustamp = stamp,
            datestamp = stamp
         });
      }

      /// <summary>
      /// Mengubah masa berlaku penugasan yang sudah ada, tanpa mencabut dan memberikannya ulang -
      /// yang akan menghapus kapan penugasan itu pertama kali dibuat.
      /// </summary>
      /// <param name="member">User pemegang penugasan tersebut.</param>
      /// <param name="start">Awal masa berlaku yang baru; <c>null</c> berarti sejak dibuat.</param>
      /// <param name="expiry">Akhir masa berlaku yang baru; <c>null</c> berarti tanpa akhir.</param>
      public async Task SetMemberPeriod(User member, DateTime? start, DateTime? expiry) {
         EnsureSaved();
         EnsureNotSystemAccount(member);

         // Baris lamanya dibaca dulu, bukan disusun ulang dari nol: datestamp-nya menyimpan kapan
         // penugasan ini dibuat, dan menulis baris baru di atasnya akan menggantinya dengan
         // sekarang - seolah orang ini baru saja diberi role.
         var assignments = await Service.GetTa_UserRoles_ByRoleId(cRoleId);
         var assignment = assignments.SingleOrDefault(r => r.cUserId == member.cUserId)
            ?? throw new InvalidOperationException(
               $"User '{member.cUserAccount}' does not hold role '{cRoleName}'.");

         assignment.cUserRoleStart = start;
         assignment.cUserRoleExpiry = expiry;
         assignment.ustamp = await App.GetDateStampAsync();
         await Service.PostTa_UserRole_Update(assignment);
      }

      /// <summary>Mencabut role ini dari seorang user.</summary>
      public Task RemoveMember(User member) {
         EnsureSaved();
         EnsureNotSystemAccount(member);

         return Service.PostTa_UserRole_Delete(new ta_UserRole {
            cUserId = member.cUserId,
            cRoleId = cRoleId
         });
      }

      #endregion

      #region Content

      /// <summary>
      /// Mengirim seluruh perubahan isi role ini - hak dan anggotanya - dalam satu panggilan.
      /// Satu-satunya pintu yang dipakai layar pengelola role; <see cref="AddClaim"/> dan
      /// kawan-kawannya tetap ada untuk pemanggil yang memang hanya mengubah satu hal.
      /// <para>
      /// Tidak menyimpan keadaan apa pun: yang diterima adalah selisihnya, bukan keadaan yang
      /// diinginkan, sehingga yang memegang "sebelum" dan "sesudah" tetap pemanggil. Seluruh
      /// operasinya tahan diulang - kalau pengiriman putus di tengah jalan, mengirim selisih yang
      /// sama sekali lagi akan menuntaskannya tanpa menggandakan apa pun.
      /// </para>
      /// </summary>
      /// <param name="set">Selisih yang dikirim. Yang kosong tidak menghasilkan permintaan apa pun.</param>
      /// <exception cref="InvalidOperationException">
      /// Kalau role ini belum pernah tersimpan, atau kalau <paramref name="set"/> ternyata milik role lain.
      /// </exception>
      public async Task SaveContentAsync(RoleSet set) {
         ArgumentNullException.ThrowIfNull(set);
         EnsureSaved();

         if (set.cRoleId != cRoleId) {
            throw new InvalidOperationException(
               $"This change set belongs to role '{set.cRoleId}', not to '{cRoleId}'.");
         }

         if (set.IsEmpty) return;

         // Pencabutan dikirim lebih dulu supaya hak yang dicabut sekaligus diberikan ulang dengan
         // masa berlaku berbeda tidak saling menimpa urutannya.
         if (set.ClaimsRevoked.Length > 0) await Service.PostTa_RoleClaim_DeleteBatch(set.ClaimsRevoked);
         if (set.ClaimsGranted.Length > 0) await Service.PostTa_RoleClaim_NewBatch(set.ClaimsGranted);

         if (set.MembersRemoved.Length > 0) await Service.PostTa_UserRole_DeleteBatch(set.MembersRemoved);
         if (set.MembersAdded.Length > 0) await Service.PostTa_UserRole_NewBatch(set.MembersAdded);

         // Satu per satu, karena tidak ada action batch untuk perubahan masa berlaku - dan yang
         // diubah masa berlakunya dalam satu kali simpan memang jarang lebih dari satu dua baris.
         foreach (var assignment in set.MembersRescheduled) {
            await Service.PostTa_UserRole_Update(assignment);
         }

         // Angka di baris rail ikut bergerak tanpa membaca ulang seluruh halaman. Dihitung dari
         // selisih yang barusan terkirim, jadi ia tidak bisa menyimpang dari apa yang benar-benar
         // berubah di server.
         ClaimCount += set.ClaimsGranted.Length - set.ClaimsRevoked.Length;
         MemberCount += set.MembersAdded.Length - set.MembersRemoved.Length;
      }

      #endregion

      #region Methods

      /// <summary>
      /// Menghapus role ini sungguhan - bukan menonaktifkannya. Hak yang dibawanya dan seluruh
      /// penugasannya ikut hilang, dan tidak ada jalan mengembalikannya.
      /// </summary>
      public Task DeleteAsync() {
         EnsureSaved();
         return Service.PostTa_Role_Delete(ToEntity());
      }

      /// <summary>
      /// Membuat role baru bernama <paramref name="newName"/> yang membawa hak yang sama persis
      /// dengan role ini. Anggotanya <b>tidak</b> ikut disalin: yang disalin adalah bentuk sebuah
      /// jabatan, bukan siapa yang sedang memegangnya.
      /// </summary>
      public async Task<Role> DuplicateAsync(string newName) {
         EnsureSaved();

         var copy = CreateNewRole(App);
         copy.cRoleName = newName;
         copy.cRoleState = cRoleState;
         copy.cRoleDescription = cRoleDescription;
         await copy.SaveAsync();

         var rows = await Service.GetTa_RoleClaims_ByRoleId(cRoleId);
         if (rows.Length > 0) {
            // Satu permintaan untuk seluruh hak, bukan satu per hak: role yang pantas digandakan
            // justru yang isinya banyak.
            await Service.PostTa_RoleClaim_NewBatch([
               .. rows.Select(r => new ta_RoleClaim { cRoleId = copy.cRoleId, cClaimName = r.cClaimName })
            ]);
            copy.ClaimCount = rows.Length;
         }

         return copy;
      }

      // Isi role tinggal di baris lain yang menunjuk id role ini, jadi tidak satu pun bisa ditulis
      // sebelum id itu ada. Ditolak di sini supaya jelas apa yang kurang, bukan dibiarkan jadi
      // pelanggaran foreign key dari database atas id penanda yang masih terbaca "Save Role...".
      private void EnsureSaved() {
         if (IsBlank) {
            throw new InvalidOperationException(
               "This role has not been saved yet, so it has no id for its claims and members to point at.");
         }
      }

      // Alasannya sama dengan penjaga bernama sama di User: akun debugger dan akun administrator
      // bawaan berdiri sebagai pengganti user yang login tanpa punya baris user sendiri, jadi tidak
      // ada yang bisa ditunjuk baris penugasan - dan mereka pun sudah berhak atas segalanya.
      private static void EnsureNotSystemAccount(User member) {
         if (member.cUserId is Defaults.DebuggerUserId or Defaults.AdminUserId) {
            throw new SystemAccountException(
               $"User '{member.cUserId}' is a system account and cannot be given a role.",
               member.cUserId);
         }
      }

      #endregion
   }
}
