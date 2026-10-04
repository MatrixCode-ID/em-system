using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Application = System.Windows.Application;
using MessageBoxResult = System.Windows.MessageBoxResult;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   public partial class RoleManager : UserControl, INavigationBody
   {
      private readonly EmApp _app;

      public RoleManager(EmApp app) {
         _app = app;
         InitializeComponent();
         Vm.EmApp = _app;
      }

      public RoleManagerVm Vm => (RoleManagerVm)DataContext;

      // Nothing is read here: the host raises this on every way into the screen, back and forward
      // included, and those two return to a rail that is already filled. Filling it belongs to
      // OnReloadRequested, which only a real navigation raises.
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      // The third of the three ways out of an unsaved role - the other two, picking another role
      // and turning the page, are held inside the view model. All three ask the same question and
      // read the same answer; only the way of refusing differs, and here it is this flag.
      public async Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (await Vm.ConfirmLeavePendingAsync()) return;

         args.Cancel = true;
         args.Message = "This role still has changes that have not been saved.";
      }

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   /// <summary>
   /// Layar pengelola role: rail berisi daftar role di kiri, dan di kanan satu role yang terbuka
   /// berikut hak, anggota, dan catatan tentangnya.
   /// <para>
   /// Isi role tidak langsung ditulis. Yang diedit di layar ini adalah hak akses, jadi mencabut satu
   /// hak karena salah klik tidak boleh langsung terkirim: yang dipegang view model ini adalah
   /// <i>baseline</i> (keadaan role saat dibuka) dan <i>desired</i> (keadaan yang diinginkan), dan
   /// selisih keduanya baru dihitung serta dikirim saat tombol simpan ditekan.
   /// </para>
   /// </summary>
   public class RoleManagerVm : MvvmModelBase
   {
      private const string DateFormat = "dd MMM yyyy";

      private RoleCollection? _collection;

      // Isi role yang sudah pernah dibaca, disimpan selama layar hidup dan dibuang saat reload:
      // membandingkan dua role berarti bolak-balik antara keduanya, dan tiga permintaan ke server
      // per klik terlalu mahal untuk dibayar berulang.
      private readonly Dictionary<string, RoleContent> _content = [];

      // Keadaan role yang sedang terbuka, seperti yang tersimpan di server.
      private readonly HashSet<string> _baselineClaims = new(StringComparer.OrdinalIgnoreCase);
      private readonly Dictionary<string, ta_UserRole> _baselineMembers = [];

      // Selisih baseline terhadap desired, dihitung ulang setiap kali salah satu berubah. Angka di
      // action bar dibaca dari sini, jadi tidak ada penghitung kedua yang bisa melenceng darinya.
      private RoleSet? _pending;

      // Waktu server, dibaca sekali per reload: keterangan ACTIVE / SCHEDULED / EXPIRED di setiap
      // baris anggota dihitung terhadapnya, dan menanyakannya per baris berarti satu perjalanan ke
      // server untuk setiap orang di dalam daftar.
      private DateTime _serverNow = DateTime.Now;

      // Menyala selama view model sendiri yang memindahkan pilihan di rail, supaya penjaga
      // perpindahan tidak menanyakan lagi apa yang barusan dijawab.
      private bool _selectionSuspended;

      // Menyala selama desired disusun ulang dari baseline, supaya menyalakan seratus centang
      // sekaligus tidak menghitung ulang selisihnya seratus kali.
      private bool _rebuildingDesired;

      // Yang memadamkan kabar jatuhan terakhir. Dibuat sekali lalu dipakai ulang: setiap jatuhan
      // baru mengulang hitungannya dari awal, sehingga menyeret beberapa hak berturut-turut tidak
      // memadamkan kabar yang barusan terbit.
      private DispatcherTimer? _dropFeedbackTimer;

      private Role? _watchedRole;

      public RoleManagerVm() {
         RegisterCommand(nameof(NewRoleCommand), NewRoleCommand, NewRoleCommandAllowed);
         RegisterCommand(nameof(DuplicateRoleCommand), DuplicateRoleCommand, DuplicateRoleCommandAllowed);
         RegisterCommand(nameof(EditDetailsCommand), EditDetailsCommand, EditDetailsCommandAllowed);
         RegisterCommand(nameof(SaveChangesCommand), SaveChangesCommand, SaveChangesCommandAllowed);
         RegisterCommand(nameof(DiscardCommand), DiscardCommand, DiscardCommandAllowed);
         RegisterCommand<ClaimItemVm?>(nameof(RevokeClaimCommand), RevokeClaimCommand, RevokeClaimCommandAllowed);
         RegisterCommand<ClaimGroupVm?>(nameof(RevokeModuleCommand), RevokeModuleCommand, RevokeModuleCommandAllowed);
         RegisterCommand<object?>(nameof(GrantDroppedCommand), GrantDroppedCommand, GrantDroppedCommandAllowed);
         RegisterCommand<MemberRowVm?>(nameof(RemoveMemberCommand), RemoveMemberCommand, RemoveMemberCommandAllowed);
         RegisterCommand(nameof(DisableRoleCommand), DisableRoleCommand, DisableRoleCommandAllowed);
         RegisterCommand(nameof(DeleteRoleCommand), DeleteRoleCommand, DeleteRoleCommandAllowed);
         RegisterCommand(nameof(PreviousPageCommand), PreviousPageCommand, PreviousPageCommandAllowed);
         RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);

         // Keduanya dibaca dari isi rail, jadi mereka ikut isinya - bukan ikut jalur yang kebetulan
         // mengubahnya. Satu baris draft yang masuk atau keluar pun sudah cukup untuk menggeser
         // keterangan di kaki rail dan menyingkirkan pesan "belum ada role".
         Roles.CollectionChanged += (_, _) => {
            NotifyChanged(nameof(HasNoRole));
            NotifyChanged(nameof(RailCaption));
         };

         // Sama alasannya: keterangan hak dibaca dari daftar grup, jadi ia ikut daftarnya.
         GrantedGroups.CollectionChanged += (_, _) => {
            NotifyChanged(nameof(GrantedSummaryCaption));
            NotifyChanged(nameof(ClaimDetailCaption));
         };

         MemberRows.CollectionChanged += (_, _) => NotifyChanged(nameof(MemberDetailCaption));
         ClaimGroups.CollectionChanged += (_, _) => NotifyChanged(nameof(CatalogueSummaryCaption));
      }

      #region Data

      /// <summary>
      /// Role yang tampil di rail: satu halaman hasil pembacaan terakhir, ditambah baris draft yang
      /// belum pernah tersimpan kalau memang sedang ada.
      /// </summary>
      public ObservableCollection<Role> Roles { get; } = [];

      /// <summary>
      /// Seluruh katalog hak yang dikenal aplikasi, dikelompokkan per module. Dibaca sekali per
      /// reload, bukan per role - katalognya sama untuk role mana pun.
      /// </summary>
      public ObservableCollection<ClaimGroupVm> ClaimGroups { get; } = [];

      /// <summary>
      /// Bagian dari <see cref="ClaimGroups"/> yang benar-benar membawa hak pada role yang terbuka.
      /// Inilah yang digambar tab Permissions: module yang role ini tidak punya urusan dengannya
      /// tidak perlu memakan tempat di sana.
      /// </summary>
      public ObservableCollection<ClaimGroupVm> GrantedGroups { get; } = [];

      /// <summary>Anggota role yang terbuka - keadaan yang diinginkan, bukan yang tersimpan.</summary>
      public ObservableCollection<MemberRowVm> MemberRows { get; } = [];

      /// <summary>
      /// Role yang sedang terbuka di pane kanan. Mengubahnya saat masih ada perubahan yang belum
      /// disimpan akan memunculkan pertanyaan lebih dulu, dan pilihannya dihormati: rail tidak
      /// berpindah sebelum perubahannya benar-benar tersimpan atau dibuang.
      /// </summary>
      public Role? SelectedRole {
         get => Get<Role?>();
         set {
            var current = Get<Role?>();
            if (ReferenceEquals(current, value)) return;

            if (_selectionSuspended || !HasPendingChanges) {
               Set(value, opened => { _ = OpenRoleAsync(opened); });
               return;
            }

            // Rail-nya sudah terlanjur berpindah saat kita sampai di sini, dan jawaban atas
            // pertanyaannya bisa memakan satu perjalanan ke server - jadi barisnya dikembalikan
            // dulu, lalu dipindahkan sungguhan hanya kalau perubahannya memang sudah beres.
            RestoreSelection();
            _ = MoveSelectionAsync(value);
         }
      }

      /// <summary><c>true</c> kalau tidak ada satu pun role di rail.</summary>
      public bool HasNoRole => Roles.Count == 0;

      /// <summary><c>true</c> kalau ada role yang terbuka di pane kanan.</summary>
      public bool HasSelectedRole => SelectedRole is not null;

      #endregion

      #region Header & details

      /// <summary>
      /// Menyala saat nama dan keterangan role sedang diedit di header pane. Yang menutupnya adalah
      /// tombol simpan di action bar, bukan tombol OK tersendiri - nama ikut alur simpan yang sama
      /// dengan hak dan anggota. Untuk role baru mode ini menyala paksa: nama role wajib diisi.
      /// </summary>
      public bool IsEditingDetails {
         get => Get<bool>();
         private set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Keterangan singkat keadaan role yang terbuka, untuk chip di sebelah namanya.</summary>
      public string StateCaption => SelectedRole?.cRoleState == RoleState.Active ? "IN USE" : "OFF";

      /// <summary>Keterangan jumlah anggota role yang terbuka, mis. "4 members".</summary>
      public string MemberCountCaption => Plural(SelectedRole?.MemberCount ?? 0, "member");

      /// <summary>Keterangan jumlah hak role yang terbuka, mis. "12 claims granted".</summary>
      public string ClaimCountCaption => $"{Plural(SelectedRole?.ClaimCount ?? 0, "claim")} granted";

      /// <summary>Kapan role yang terbuka terakhir berubah, seperti yang dicatat barisnya sendiri.</summary>
      public string LastChangedCaption =>
         SelectedRole is { IsBlank: false } role && role.ustamp != default
            ? role.ustamp.ToString($"{DateFormat}, HH:mm")
            : "Not saved yet";

      /// <summary>
      /// Kalimat utuhnya untuk baris fakta di header. Disusun di sini, bukan dirangkai di XAML dari
      /// sepotong kata dan sepotong nilai: baris yang belum pernah tersimpan tidak punya "kapan"
      /// untuk disebut, dan menempelkan kata "Last changed" di depannya menghasilkan kalimat yang
      /// tidak berarti apa-apa.
      /// </summary>
      public string LastChangedLine =>
         SelectedRole is { IsBlank: false } ? $"Last changed {LastChangedCaption}" : "Not saved yet";

      /// <summary>Id role yang terbuka, atau penanda yang terbaca orang selama id-nya belum terbit.</summary>
      public string IdentifierCaption => SelectedRole?.cRoleId ?? string.Empty;

      /// <summary>Keadaan role yang terbuka, dieja penuh untuk tab Details.</summary>
      public string StateDetailCaption => SelectedRole?.cRoleState == RoleState.Active ? "In use" : "Disabled";

      /// <summary>Ringkasan anggota untuk tab Details, mis. "4 accounts, 1 of them scheduled".</summary>
      public string MemberDetailCaption {
         get {
            if (SelectedRole is null) return string.Empty;

            var accounts = Plural(MemberRows.Count, "account");
            var scheduled = MemberRows.Count(r => r.State == MemberPeriodState.Scheduled);
            return scheduled == 0 ? accounts : $"{accounts}, {scheduled} of them scheduled";
         }
      }

      /// <summary>Ringkasan hak untuk tab Details, mis. "12 claims, from 4 modules".</summary>
      public string ClaimDetailCaption {
         get {
            if (SelectedRole is null) return string.Empty;

            var claims = GrantedGroups.Sum(g => g.GrantedCount);
            return claims == 0
               ? "No claim granted"
               : $"{Plural(claims, "claim")}, from {Plural(GrantedGroups.Count, "module")}";
         }
      }

      /// <summary>Keterangan jumlah hak untuk kepala tab Permissions, mis. "12 in 5 modules".</summary>
      public string GrantedSummaryCaption {
         get {
            var claims = GrantedGroups.Sum(g => g.GrantedCount);
            return claims == 0 ? "none yet" : $"{claims} in {Plural(GrantedGroups.Count, "module")}";
         }
      }

      /// <summary>Keterangan isi katalog untuk kaki lembar katalog, mis. "20 claims in 5 modules".</summary>
      public string CatalogueSummaryCaption =>
         $"{Plural(ClaimGroups.Sum(g => g.Claims.Count), "claim")} in {Plural(ClaimGroups.Count, "module")}";

      /// <summary>
      /// Alasan tab Permissions dan Members terkunci selama role belum tersimpan. Bukan sekadar
      /// mematikan tabnya: yang perlu dibaca adalah kenapa, bukan cuma bahwa ia mati.
      /// </summary>
      public string DraftLockCaption {
         get {
            var pendingClaims = _pending?.ClaimsGranted.Length ?? 0;
            return pendingClaims == 0
               ? "A role has to be saved before it can carry anything: its claims and its members point at an id that has not been issued yet."
               : $"A role has to be saved before it can carry anything. The {Plural(pendingClaims, "claim")} copied from the original will be granted the moment this role is saved.";
         }
      }

      /// <summary>
      /// Membuka atau melipat seluruh grup module di tab Permissions sekaligus. Grup yang sesudah
      /// itu dilipat sendiri oleh pembacanya berpisah jalan dengan tombol ini sampai tombolnya
      /// ditekan lagi - persis seperti yang dijanjikan tulisan di tombolnya.
      /// </summary>
      public bool ArePermissionGroupsExpanded {
         get => Get(true);
         set => Set(value, expanded => {
            foreach (var group in ClaimGroups) group.IsExpanded = expanded;
         });
      }

      /// <summary>
      /// Pasangannya untuk lembar katalog. Terpisah karena keduanya memang dua tombol yang berbeda
      /// di dua daftar yang berbeda.
      /// </summary>
      public bool AreCatalogueGroupsExpanded {
         get => Get(true);
         set => Set(value, expanded => {
            foreach (var group in ClaimGroups) group.IsCatalogueExpanded = expanded;
         });
      }

      /// <summary>
      /// Apa yang terjadi pada jatuhan terakhir, mis. "3 of 5 claims from core.contact granted".
      /// <para>
      /// Angka di kepala tab memang sudah bergerak sendiri, tapi jatuhan yang tidak mengubah apa
      /// pun - hak yang sudah dipegang, module yang sudah penuh - tidak menggerakkan apa pun juga,
      /// dan tanpa sepatah kata pun ia terbaca sebagai drag yang gagal.
      /// </para>
      /// </summary>
      public string DropFeedbackCaption {
         get => Get(string.Empty);
         set => Set(value, _ => NotifyChanged(nameof(HasDropFeedback)));
      }

      /// <summary>Apakah ada kabar jatuhan yang masih perlu ditampilkan.</summary>
      public bool HasDropFeedback => !string.IsNullOrEmpty(DropFeedbackCaption);

      #endregion

      #region Paging & counters

      /// <summary>Banyaknya role seluruhnya di server, untuk chip di toolbar.</summary>
      public string TotalCaption => Plural(_collection?.TotalCount ?? 0, "role");

      /// <summary>Banyaknya penugasan role seluruhnya di server, untuk chip di toolbar.</summary>
      public string AssignmentCaption => Plural(_collection?.TotalAssignments ?? 0, "assignment");

      /// <summary>Keterangan isi rail, mis. "6 of 24 roles".</summary>
      public string RailCaption {
         get {
            var total = _collection?.TotalCount ?? 0;
            return total == 0 ? "No role to show" : $"{Roles.Count} of {Plural(total, "role")}";
         }
      }

      private int PageCount {
         get {
            if (_collection is not { TotalCount: > 0, PageSize: > 0 }) return 1;

            var pages = (_collection.TotalCount + _collection.PageSize - 1) / _collection.PageSize;
            return pages < 1 ? 1 : pages;
         }
      }

      #endregion

      #region Pending changes

      /// <summary>
      /// <c>true</c> kalau ada yang belum tersimpan pada role yang terbuka - baris draft yang belum
      /// pernah lahir, kolom role yang berubah, atau selisih hak dan anggota.
      /// </summary>
      public bool HasPendingChanges =>
         SelectedRole is { } role && (role.IsBlank || role.IsDirty || _pending is { IsEmpty: false });

      /// <summary>Kalimat di action bar yang menyebut apa saja yang berubah sejak role ini dibuka.</summary>
      public string ChangeCaption {
         get {
            if (SelectedRole is not { } role) return string.Empty;
            if (role.IsBlank) return "This role has not been saved yet, so nothing it carries can be written.";

            var parts = new List<string>();
            if (role.IsDirty) parts.Add("the details");
            if (_pending is { ClaimChangeCount: > 0 } claims) parts.Add(Plural(claims.ClaimChangeCount, "claim"));
            if (_pending is { AssignmentChangeCount: > 0 } members) parts.Add(Plural(members.AssignmentChangeCount, "assignment"));

            if (parts.Count == 0) return "Nothing has changed since this role was opened.";

            var subject = parts.Count switch {
               1 => parts[0],
               2 => $"{parts[0]} and {parts[1]}",
               _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
            };

            return $"{char.ToUpper(subject[0])}{subject[1..]} changed since this role was opened.";
         }
      }

      #endregion

      #region Commands

      /// <summary>
      /// Membuat role baru tanpa dialog: barisnya muncul di puncak rail, hidup di memory, dan baru
      /// lahir di database saat tombol simpan ditekan.
      /// </summary>
      public async Task NewRoleCommand() {
         if (EmApp == null || !await ConfirmLeavePendingAsync()) return;

         AddDraft(Role.CreateNewRole(EmApp));
      }

      /// <summary>Hanya boleh dijalankan kalau daftarnya sudah termuat dan tidak ada yang sedang berjalan.</summary>
      public bool NewRoleCommandAllowed() => _collection is not null && !IsBusy;

      /// <summary>
      /// Menggandakan role yang terbuka menjadi baris draft baru: nama, keterangan, dan seluruh
      /// haknya ikut, anggotanya tidak - yang digandakan adalah bentuk sebuah jabatan, bukan siapa
      /// yang sedang memegangnya. Sama seperti New Role, tidak ada apa pun yang menyentuh server
      /// sampai tombol simpan ditekan.
      /// </summary>
      public async Task DuplicateRoleCommand() {
         if (EmApp == null || SelectedRole is not { IsBlank: false } source) return;

         // Dibaca sekarang, selagi role sumbernya masih terbuka: begitu draft-nya terpilih, desired
         // yang ada di layar adalah milik draft itu.
         var claims = ClaimGroups.SelectMany(g => g.Claims).Where(c => c.IsGranted).Select(c => c.Key).ToArray();

         if (!await ConfirmLeavePendingAsync()) return;

         var draft = Role.CreateNewRole(EmApp);
         draft.cRoleName = $"{source.cRoleName} (copy)";
         draft.cRoleDescription = source.cRoleDescription;
         draft.cRoleState = source.cRoleState;
         draft.UiIcon = source.UiIcon;

         AddDraft(draft, claims);
      }

      /// <summary>Hanya boleh dijalankan kalau yang terbuka adalah role yang sudah tersimpan.</summary>
      public bool DuplicateRoleCommandAllowed() => SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>Membuka mode edit nama dan keterangan di header pane.</summary>
      public void EditDetailsCommand() => IsEditingDetails = true;

      /// <summary>
      /// Hanya boleh dijalankan kalau ada role terbuka yang sudah tersimpan dan modenya belum
      /// menyala. Untuk role baru tombolnya mati - modenya memang sudah menyala dan tidak bisa
      /// dimatikan.
      /// </summary>
      public bool EditDetailsCommandAllowed() => SelectedRole is { IsBlank: false } && !IsEditingDetails;

      /// <summary>
      /// Mengirim seluruh perubahan role yang terbuka: kolomnya sendiri lewat jalur biasa, isinya
      /// lewat satu pintu <see cref="Role.SaveContentAsync"/>.
      /// <para>
      /// Kalau pengirimannya gagal, tidak ada apa pun di layar yang disentuh: baseline tetap
      /// baseline, desired tetap desired, dan menekan simpan sekali lagi akan menghitung selisih
      /// yang sama persis lalu mengirimkannya ulang.
      /// </para>
      /// </summary>
      public async Task SaveChangesCommand() {
         if (EmApp == null || SelectedRole is not { } role) return;

         try {
            WaiterText = "Saving role...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            var wasBlank = role.IsBlank;
            if (wasBlank && _collection != null) await _collection.NewRoleAsync(role);
            else if (wasBlank || role.IsDirty) await role.SaveAsync();

            var stamp = await EmApp.GetDateStampAsync();
            var set = BuildRoleSet(role, stamp);
            if (!set.IsEmpty) await role.SaveContentAsync(set);

            AcceptAsBaseline(role, set, stamp);
            IsEditingDetails = false;

            if (wasBlank) _content.Remove(role.cRoleId);
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RefreshPendingState();
            RaiseRoleDerived();
         }
      }

      /// <summary>Hanya boleh dijalankan kalau memang ada yang belum tersimpan.</summary>
      public bool SaveChangesCommandAllowed() => HasPendingChanges && !IsBusy;

      /// <summary>
      /// Mengembalikan role yang terbuka ke keadaan saat ia dibuka, sekaligus menutup mode edit
      /// nama dan keterangan. Untuk baris draft yang berarti membuang barisnya dari rail:
      /// <see cref="UiModel{TEntity,TService}.RollBack"/> hanya mengembalikan kolom ke data
      /// aslinya, dan data asli sebuah draft adalah baris kosong berisi penanda - membuang
      /// objeknya adalah urusan layar.
      /// </summary>
      public void DiscardCommand() {
         if (SelectedRole is not { } role) return;

         if (role.IsBlank) {
            RemoveDraft(role);
            return;
         }

         role.RollBack();
         RebuildDesired(role);
         IsEditingDetails = false;
         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>
      /// Boleh dijalankan kalau memang ada yang bisa dibatalkan - dan juga selama mode edit nama
      /// dan keterangan menyala meski belum ada yang diketik, karena tombol inilah satu-satunya
      /// jalan menutup mode itu di tempat. Tanpa itu, menekan Edit details lalu berubah pikiran
      /// mematikan ketiga tombol sekaligus, dan membatalkan niatnya harus dibayar dengan pindah
      /// dari role yang sedang dibuka.
      /// </summary>
      public bool DiscardCommandAllowed() => (HasPendingChanges || IsEditingDetails) && !IsBusy;

      /// <summary>
      /// Mencabut satu hak dari role yang terbuka - di layar saja. Yang berubah adalah keadaan yang
      /// diinginkan; servernya baru mendengar saat tombol simpan ditekan.
      /// </summary>
      public void RevokeClaimCommand(ClaimItemVm? claim) {
         if (claim is not null) claim.IsGranted = false;
      }

      /// <summary>Hanya boleh dijalankan untuk hak yang memang sedang dipegang.</summary>
      public bool RevokeClaimCommandAllowed(ClaimItemVm? claim) =>
         claim is { IsGranted: true } && SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Mencabut seluruh hak satu module dari role yang terbuka sekaligus - kebalikan dari
      /// menjatuhkan kartu module ke tab ini, dan seperti pencabutan satuan ia hanya menggeser
      /// keadaan yang diinginkan. Kartunya lenyap dari tab Permissions begitu hak terakhirnya
      /// dilepas, karena tab ini memang hanya menggambar module yang role ini punya urusan
      /// dengannya. Tidak ditanyakan lebih dulu: tidak ada yang terkirim sampai tombol simpan
      /// ditekan, dan Discard mengembalikan semuanya.
      /// </summary>
      public void RevokeModuleCommand(ClaimGroupVm? group) {
         if (group is null) return;

         // Disalin lebih dulu: Granted disusun ulang setiap kali satu hak padam, jadi mencabut
         // sambil menelusurinya berarti menelusuri daftar yang menyusut di bawah tangan sendiri.
         var held = group.Granted.ToArray();
         if (held.Length == 0) return;

         _rebuildingDesired = true;
         try {
            foreach (var claim in held) claim.IsGranted = false;
         }
         finally {
            _rebuildingDesired = false;
         }

         group.RefreshGranted();
         RefreshGrantedGroups();
         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>Hanya boleh dijalankan untuk module yang memang sedang membawa hak.</summary>
      public bool RevokeModuleCommandAllowed(ClaimGroupVm? group) =>
         group is { GrantedCount: > 0 } && SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Memberikan hak yang barusan diseret dari katalog ke role yang terbuka - di layar saja,
      /// seperti pencabutan. Muatannya bisa satu hak (<see cref="ClaimItemVm"/>) atau satu module
      /// utuh (<see cref="ClaimGroupVm"/>), dan keduanya masuk lewat pintu yang sama karena yang
      /// diseret orangnya memang satu hal: apa yang ada di bawah kursornya.
      /// </summary>
      public void GrantDroppedCommand(object? payload) {
         switch (payload) {
            case ClaimItemVm claim:
               GrantOne(claim);
               break;

            case ClaimGroupVm group:
               GrantModule(group);
               break;
         }
      }

      /// <summary>
      /// Boleh dijalankan untuk muatan yang memang dikenal layar ini, selama ada role tersimpan
      /// yang terbuka. Module yang seluruh haknya sudah dipegang tetap dijawab boleh: ia memang
      /// tidak akan mengubah apa pun, tapi itu dikatakan sesudah dijatuhkan, bukan dengan kursor
      /// "tidak boleh" yang membuat orangnya mengira drag-nya yang gagal.
      /// </summary>
      public bool GrantDroppedCommandAllowed(object? payload) =>
         payload is ClaimItemVm or ClaimGroupVm && SelectedRole is { IsBlank: false } && !IsBusy;

      // Satu hak: menyalakan saklarnya sudah cukup, karena setternya sendiri yang menyusun ulang
      // tab Permissions dan menghitung ulang selisihnya.
      private void GrantOne(ClaimItemVm claim) {
         if (claim.IsGranted) {
            ShowDropFeedback($"{claim.Key} is already on this role");
            return;
         }

         claim.IsGranted = true;
         ShowDropFeedback($"{claim.Key} granted");
      }

      // Satu module: yang belum dipegang dikumpulkan lebih dulu, baru dinyalakan bersama-sama.
      // Tanpa itu penyegaran tab dan penghitungan selisihnya dibayar sekali per hak - dan yang
      // dilihat orangnya tetap sama persis.
      private void GrantModule(ClaimGroupVm group) {
         var wanted = group.Claims.Where(c => !c.IsGranted).ToArray();
         if (wanted.Length == 0) {
            ShowDropFeedback($"{group.ModuleName} is already fully granted");
            return;
         }

         _rebuildingDesired = true;
         try {
            foreach (var claim in wanted) claim.IsGranted = true;
         }
         finally {
            _rebuildingDesired = false;
         }

         group.RefreshGranted();
         RefreshGrantedGroups();
         RefreshPendingState();
         RaiseRoleDerived();

         ShowDropFeedback(
            $"{wanted.Length:N0} of {Plural(group.Claims.Count, "claim")} from {group.ModuleName} granted");
      }

      private void ShowDropFeedback(string caption) {
         DropFeedbackCaption = caption;

         _dropFeedbackTimer ??= BuildDropFeedbackTimer();
         _dropFeedbackTimer.Stop();
         _dropFeedbackTimer.Start();
      }

      private DispatcherTimer BuildDropFeedbackTimer() {
         var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
         timer.Tick += (_, _) => ClearDropFeedback();
         return timer;
      }

      // Dipanggil juga saat role berpindah dan saat layar dibaca ulang: kalimat yang menyebut role
      // sebelumnya tidak boleh tertinggal di layar role berikutnya.
      private void ClearDropFeedback() {
         _dropFeedbackTimer?.Stop();
         DropFeedbackCaption = string.Empty;
      }

      /// <summary>
      /// Mengeluarkan satu anggota dari role yang terbuka - di layar saja, seperti pencabutan hak.
      /// </summary>
      public void RemoveMemberCommand(MemberRowVm? member) {
         if (member is null || !MemberRows.Remove(member)) return;

         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>Hanya boleh dijalankan untuk baris anggota yang memang ada di daftar.</summary>
      public bool RemoveMemberCommandAllowed(MemberRowVm? member) =>
         member is not null && SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Menonaktifkan role yang terbuka, atau menyalakannya kembali. Keduanya hanya mengubah kolom
      /// keadaan, jadi mereka ikut alur simpan yang sama dengan seluruh isi layar ini - bukan
      /// perintah yang langsung terkirim sendiri.
      /// </summary>
      public void DisableRoleCommand() {
         if (SelectedRole is not { } role) return;

         role.cRoleState = role.cRoleState == RoleState.Active ? RoleState.Disabled : RoleState.Active;
         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>Hanya boleh dijalankan untuk role yang sudah tersimpan.</summary>
      public bool DisableRoleCommandAllowed() => SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Menghapus role yang terbuka sungguhan, berikut seluruh hak dan penugasannya. Ditanyakan
      /// lebih dulu, dan tidak lewat action bar: ini satu-satunya hal di layar ini yang tidak bisa
      /// dibatalkan.
      /// </summary>
      public async Task DeleteRoleCommand() {
         if (_collection == null || SelectedRole is not { IsBlank: false } role) return;

         var owner = DialogOwner;
         if (owner == null) return;

         var answer = owner.ShowMboxDecideWarning(
            $"Delete the role '{role.cRoleName}'? It will be taken off the {Plural(role.MemberCount, "account")} that carry it, and this cannot be undone.",
            "Delete role");
         if (answer != MessageBoxResult.Yes) return;

         try {
            WaiterText = "Deleting role...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            await _collection.RemoveAsync(role);
            _content.Remove(role.cRoleId);
            Roles.Remove(role);
            await SelectFirstRoleAsync();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseListDerived();
         }
      }

      /// <summary>Hanya boleh dijalankan untuk role yang sudah tersimpan.</summary>
      public bool DeleteRoleCommandAllowed() => SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>Mundur satu halaman di rail.</summary>
      public Task PreviousPageCommand() => GoToPageAsync((_collection?.Page ?? 1) - 1);

      /// <summary>Hanya boleh dijalankan kalau masih ada halaman sebelum halaman yang terbuka.</summary>
      public bool PreviousPageCommandAllowed() => _collection is { Page: > 1 } && !IsBusy;

      /// <summary>Maju satu halaman di rail.</summary>
      public Task NextPageCommand() => GoToPageAsync((_collection?.Page ?? 1) + 1);

      /// <summary>Hanya boleh dijalankan kalau masih ada halaman sesudah halaman yang terbuka.</summary>
      public bool NextPageCommandAllowed() => _collection is not null && _collection.Page < PageCount && !IsBusy;

      #endregion

      #region Reload

      /// <summary>
      /// Membaca ulang seluruh isi layar: satu halaman role, katalog hak, dan angka-angkanya. Isi
      /// role yang sempat di-cache ikut dibuang - yang diminta adalah keadaan terbaru, bukan yang
      /// sempat terbaca tadi.
      /// </summary>
      public async Task ReloadAsync() {
         if (EmApp == null || IsBusy) return;

         try {
            WaiterText = "Loading roles...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            ClearDropFeedback();
            _content.Clear();
            _serverNow = await EmApp.GetDateStampAsync();

            if (_collection == null) _collection = await RoleCollection.LoadAsync(EmApp);
            else await _collection.ReloadAsync();

            await BuildClaimCatalogueAsync();

            _selectionSuspended = true;
            try {
               Set<Role?>(null, null, nameof(SelectedRole));
               Roles.Clear();
               foreach (var role in _collection) Roles.Add(role);
            }
            finally {
               _selectionSuspended = false;
            }

            await SelectFirstRoleAsync();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseListDerived();
         }
      }

      // Katalognya sendiri tidak berubah selama aplikasi berjalan - ia dideklarasikan module saat
      // server start - tapi angka "dipegang berapa role" berubah setiap kali ada yang menyimpan,
      // jadi yang dibaca ulang setiap reload adalah angkanya, bukan katalognya.
      private async Task BuildClaimCatalogueAsync() {
         var service = EmApp!.ServiceProvider.GetRequiredService<ICredentialServices>();
         var usage = (await service.GetMeta_ClaimUsage())
            .ToDictionary(r => r.ClaimKey, r => r.RoleCount, StringComparer.OrdinalIgnoreCase);

         ClaimGroups.Clear();
         GrantedGroups.Clear();

         var groups = EmApp.AllClaims
            .GroupBy(c => c.ModuleName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

         foreach (var group in groups) {
            var item = new ClaimGroupVm(group.Key, group.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase), usage, OnClaimChanged);
            ClaimGroups.Add(item);
         }
      }

      #endregion

      #region Opening a role

      // Baseline dibaca sekali per role lalu disimpan; desired dibangun dari baseline itu. Yang
      // diikat XAML adalah desired, jadi mencentang dan membatalkan centang yang sama otomatis
      // kembali menjadi nol perubahan tanpa ada daftar niat yang harus dicari dan dibersihkan.
      private async Task OpenRoleAsync(Role? role) {
         WatchRole(role);
         ClearDropFeedback();

         if (role == null) {
            ClearContent();
            RaiseRoleDerived();
            return;
         }

         IsEditingDetails = role.IsBlank;

         if (role.IsBlank) {
            // Draft belum punya apa-apa di server - baseline-nya kosong, dan desired-nya adalah
            // apa pun yang sudah dititipkan pemanggil (mis. hasil menggandakan role lain).
            _baselineClaims.Clear();
            _baselineMembers.Clear();
            RefreshPendingState();
            RaiseRoleDerived();
            return;
         }

         // Dipanggil sendiri maupun dari tengah reload; yang memasang lapisan tunggu adalah yang
         // pertama sampai, supaya yang kedua tidak mencabutnya selagi yang pertama masih berjalan.
         var ownsWaiting = false;

         try {
            if (!_content.TryGetValue(role.cRoleId, out var content)) {
               if (!IsBusy) {
                  ownsWaiting = true;
                  WaiterText = "Loading role content...";
                  InWaiting = IsBusy = true;
                  RaiseCommandsChanged();
               }

               content = new RoleContent(
                  await role.GetRoleClaims(),
                  await role.GetMembers(),
                  await role.GetAssignments());
               _content[role.cRoleId] = content;
            }

            _baselineClaims.Clear();
            foreach (var claim in content.Claims) _baselineClaims.Add(claim.Key);

            _baselineMembers.Clear();
            foreach (var assignment in content.Assignments) _baselineMembers[assignment.cUserId] = assignment;

            RebuildDesired(role, content);
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            if (ownsWaiting) InWaiting = IsBusy = false;
            RefreshPendingState();
            RaiseRoleDerived();
         }
      }

      private void RebuildDesired(Role role, RoleContent? content = null) {
         if (role.IsBlank) return;
         if (content == null && !_content.TryGetValue(role.cRoleId, out content)) return;

         _rebuildingDesired = true;
         try {
            foreach (var claim in ClaimGroups.SelectMany(g => g.Claims))
               claim.IsGranted = _baselineClaims.Contains(claim.Key);

            MemberRows.Clear();
            foreach (var member in content.Members) {
               _baselineMembers.TryGetValue(member.cUserId, out var assignment);
               MemberRows.Add(new MemberRowVm(member, assignment, _serverNow));
            }
         }
         finally {
            _rebuildingDesired = false;
         }

         RefreshGrantedGroups();
      }

      private void ClearContent() {
         _baselineClaims.Clear();
         _baselineMembers.Clear();
         _pending = null;

         _rebuildingDesired = true;
         try {
            foreach (var claim in ClaimGroups.SelectMany(g => g.Claims)) claim.IsGranted = false;
            MemberRows.Clear();
         }
         finally {
            _rebuildingDesired = false;
         }

         RefreshGrantedGroups();
         IsEditingDetails = false;
      }

      // Role-nya sendiri yang memberi tahu kalau nama atau keadaannya berubah, sehingga penghitung
      // di action bar dan tombol simpan ikut bergerak saat orangnya sedang mengetik.
      private void WatchRole(Role? role) {
         if (ReferenceEquals(_watchedRole, role)) return;

         if (_watchedRole != null) _watchedRole.PropertyChanged -= OnSelectedRolePropertyChanged;
         _watchedRole = role;
         if (_watchedRole != null) _watchedRole.PropertyChanged += OnSelectedRolePropertyChanged;
      }

      private void OnSelectedRolePropertyChanged(object? sender, PropertyChangedEventArgs e) {
         RaiseRoleDerived();
         if (e.PropertyName is nameof(Role.IsDirty) or nameof(Role.IsBlank)) RaiseCommandsChanged();
      }

      #endregion

      #region Drafts

      private void AddDraft(Role draft, IReadOnlyCollection<string>? claims = null) {
         _selectionSuspended = true;
         try {
            Roles.Insert(0, draft);
            Set<Role?>(draft, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         WatchRole(draft);
         _baselineClaims.Clear();
         _baselineMembers.Clear();

         _rebuildingDesired = true;
         try {
            var wanted = claims?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var claim in ClaimGroups.SelectMany(g => g.Claims))
               claim.IsGranted = wanted.Contains(claim.Key);

            MemberRows.Clear();
         }
         finally {
            _rebuildingDesired = false;
         }

         RefreshGrantedGroups();
         IsEditingDetails = true;
         RefreshPendingState();
         RaiseRoleDerived();
         RaiseListDerived();
      }

      private void RemoveDraft(Role draft) {
         _selectionSuspended = true;
         try {
            Roles.Remove(draft);
            Set<Role?>(null, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         _pending = null;
         ClearContent();
         WatchRole(null);
         _ = SelectFirstRoleAsync();
         RaiseListDerived();
      }

      private Task SelectFirstRoleAsync() {
         var first = Roles.FirstOrDefault();

         _selectionSuspended = true;
         try {
            Set<Role?>(first, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         return OpenRoleAsync(first);
      }

      #endregion

      #region The guard on leaving unsaved changes

      /// <summary>
      /// Menanyakan apa yang harus dilakukan atas perubahan yang belum tersimpan, lalu menjalankan
      /// jawabannya. Dipakai ketiga jalan keluar dari sebuah role: pindah baris di rail, pindah
      /// halaman, dan pindah navigasi.
      /// </summary>
      /// <returns>
      /// <c>true</c> kalau layar boleh ditinggalkan - entah karena memang tidak ada yang tertahan,
      /// karena perubahannya sudah tersimpan, atau karena orangnya memilih membuangnya.
      /// </returns>
      public async Task<bool> ConfirmLeavePendingAsync() {
         if (!HasPendingChanges) return true;

         var owner = DialogOwner;
         if (owner == null) return true;

         var answer = owner.ShowMboxDecideCancel(
            $"{ChangeCaption}\n\nSave the changes before leaving this role?\n\nYes saves them, No throws them away, Cancel stays here.",
            "Unsaved changes");

         switch (answer) {
            case MessageBoxResult.Yes:
               await SaveChangesCommand();

               // Simpan yang gagal tidak boleh terbaca seperti simpan yang berhasil: kalau masih ada
               // yang tertahan, layarnya tetap di tempat dan perubahannya masih utuh untuk dicoba lagi.
               return !HasPendingChanges;

            case MessageBoxResult.No:
               DiscardCommand();
               return true;

            default:
               return false;
         }
      }

      private async Task MoveSelectionAsync(Role? target) {
         if (!await ConfirmLeavePendingAsync()) return;

         // Draft yang dibuang sudah membawa pilihannya sendiri ke baris lain, jadi target yang
         // sudah tidak ada di rail tidak dikejar lagi.
         if (target != null && !Roles.Contains(target)) return;

         _selectionSuspended = true;
         try {
            Set(target, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         await OpenRoleAsync(target);
      }

      private async Task GoToPageAsync(int page) {
         if (_collection == null || IsBusy) return;
         if (page < 1 || page > PageCount) return;
         if (!await ConfirmLeavePendingAsync()) return;

         try {
            WaiterText = "Loading roles...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            _content.Clear();
            await _collection.GoToPageAsync(page);

            _selectionSuspended = true;
            try {
               Set<Role?>(null, null, nameof(SelectedRole));
               Roles.Clear();
               foreach (var role in _collection) Roles.Add(role);
            }
            finally {
               _selectionSuspended = false;
            }

            await SelectFirstRoleAsync();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseListDerived();
         }
      }

      // Rail-nya sudah terlanjur memindahkan sorotannya sendiri sebelum setter dipanggil, dan
      // nilai di view model tidak berubah - jadi satu-satunya yang mengembalikannya adalah
      // notifikasi. Lewat dispatcher, supaya klik yang sedang berjalan selesai lebih dulu.
      private void RestoreSelection() {
         var dispatcher = Application.Current?.Dispatcher;
         if (dispatcher == null) {
            NotifyChanged(nameof(SelectedRole));
            return;
         }

         dispatcher.BeginInvoke(() => NotifyChanged(nameof(SelectedRole)), DispatcherPriority.Background);
      }

      #endregion

      #region Baseline versus desired

      private void OnClaimChanged(ClaimItemVm claim) {
         if (_rebuildingDesired) return;

         RefreshGrantedGroups();
         RefreshPendingState();
         RaiseRoleDerived();
      }

      // Tab Permissions hanya menggambar module yang role ini benar-benar punya urusan dengannya.
      // Urutannya mengikuti katalog, jadi sebuah module selalu muncul di tempat yang sama entah ia
      // baru saja mendapat haknya yang pertama atau sudah lama ada di sana.
      private void RefreshGrantedGroups() {
         var wanted = ClaimGroups.Where(g => g.GrantedCount > 0).ToArray();

         for (var i = GrantedGroups.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(GrantedGroups[i])) GrantedGroups.RemoveAt(i);
         }

         for (var i = 0; i < wanted.Length; i++) {
            if (i < GrantedGroups.Count && ReferenceEquals(GrantedGroups[i], wanted[i])) continue;
            GrantedGroups.Insert(i, wanted[i]);
         }
      }

      private void RefreshPendingState() {
         _pending = SelectedRole is { } role ? BuildRoleSet(role, _serverNow) : null;

         NotifyChanged(nameof(HasPendingChanges));
         NotifyChanged(nameof(ChangeCaption));
         NotifyChanged(nameof(DraftLockCaption));
         RaiseCommandsChanged();
      }

      // Selisih desired terhadap baseline. Untuk baris draft id yang terbawa masih berupa penanda -
      // itu tidak apa-apa, karena satu-satunya yang membacanya sebelum role lahir adalah penghitung
      // di action bar; yang benar-benar dikirim disusun ulang sesudah id-nya terbit.
      private RoleSet BuildRoleSet(Role role, DateTime stamp) {
         var desired = ClaimGroups
            .SelectMany(g => g.Claims)
            .Where(c => c.IsGranted)
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         var granted = desired
            .Where(key => !_baselineClaims.Contains(key))
            .Select(key => new ta_RoleClaim { cRoleId = role.cRoleId, cClaimName = key })
            .ToArray();

         var revoked = _baselineClaims
            .Where(key => !desired.Contains(key))
            .Select(key => new ta_RoleClaim { cRoleId = role.cRoleId, cClaimName = key })
            .ToArray();

         var added = new List<ta_UserRole>();
         var rescheduled = new List<ta_UserRole>();

         foreach (var row in MemberRows) {
            if (!_baselineMembers.TryGetValue(row.User.cUserId, out var original)) {
               added.Add(new ta_UserRole {
                  cUserId = row.User.cUserId,
                  cRoleId = role.cRoleId,
                  cUserRoleStart = row.Start,
                  cUserRoleExpiry = row.Expiry,
                  ustamp = stamp,
                  datestamp = stamp
               });
               continue;
            }

            if (original.cUserRoleStart == row.Start && original.cUserRoleExpiry == row.Expiry) continue;

            // datestamp baris lamanya dibawa apa adanya: ia menyimpan kapan penugasan ini pertama
            // kali dibuat, dan menulis "sekarang" di atasnya akan terbaca seolah orang ini baru saja
            // diberi role-nya.
            rescheduled.Add(new ta_UserRole {
               cUserId = original.cUserId,
               cRoleId = role.cRoleId,
               cUserRoleStart = row.Start,
               cUserRoleExpiry = row.Expiry,
               ustamp = stamp,
               datestamp = original.datestamp
            });
         }

         var kept = MemberRows.Select(r => r.User.cUserId).ToHashSet(StringComparer.Ordinal);
         var removed = _baselineMembers.Values
            .Where(r => !kept.Contains(r.cUserId))
            .Select(r => new ta_UserRole { cUserId = r.cUserId, cRoleId = role.cRoleId })
            .ToArray();

         return new RoleSet {
            cRoleId = role.cRoleId,
            ClaimsGranted = granted,
            ClaimsRevoked = revoked,
            MembersAdded = [.. added],
            MembersRemoved = removed,
            MembersRescheduled = [.. rescheduled]
         };
      }

      // Dipanggil hanya setelah pengiriman benar-benar berhasil: sejak titik ini keadaan yang
      // diinginkan itulah keadaan yang tersimpan, jadi penghitungnya kembali nol dengan sendirinya.
      private void AcceptAsBaseline(Role role, RoleSet set, DateTime stamp) {
         foreach (var claim in set.ClaimsRevoked) _baselineClaims.Remove(claim.cClaimName);
         foreach (var claim in set.ClaimsGranted) _baselineClaims.Add(claim.cClaimName);

         foreach (var member in set.MembersRemoved) _baselineMembers.Remove(member.cUserId);
         foreach (var member in set.MembersAdded) _baselineMembers[member.cUserId] = member;
         foreach (var member in set.MembersRescheduled) _baselineMembers[member.cUserId] = member;

         foreach (var row in MemberRows) {
            if (_baselineMembers.TryGetValue(row.User.cUserId, out var assignment)) row.Accept(assignment, stamp);
         }

         _content[role.cRoleId] = new RoleContent(
            [.. _baselineClaims.Select(ClaimAction.FromKey)],
            [.. MemberRows.Select(r => r.User)],
            [.. _baselineMembers.Values]);
      }

      #endregion

      #region Methods

      private static string Plural(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}";

      private void RaiseListDerived() {
         NotifyChanged(nameof(HasNoRole));
         NotifyChanged(nameof(TotalCaption));
         NotifyChanged(nameof(AssignmentCaption));
         NotifyChanged(nameof(RailCaption));
         NotifyChanged(nameof(CatalogueSummaryCaption));
         RaiseCommandsChanged();
      }

      private void RaiseRoleDerived() {
         NotifyChanged(nameof(HasSelectedRole));
         NotifyChanged(nameof(StateCaption));
         NotifyChanged(nameof(StateDetailCaption));
         NotifyChanged(nameof(MemberCountCaption));
         NotifyChanged(nameof(ClaimCountCaption));
         NotifyChanged(nameof(LastChangedCaption));
         NotifyChanged(nameof(LastChangedLine));
         NotifyChanged(nameof(IdentifierCaption));
         NotifyChanged(nameof(MemberDetailCaption));
         NotifyChanged(nameof(ClaimDetailCaption));
         NotifyChanged(nameof(GrantedSummaryCaption));
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }

      #endregion

      // Isi satu role seperti yang terbaca dari server. Disimpan utuh supaya berpindah ke role lain
      // lalu kembali tidak berarti membacanya lagi.
      private sealed record RoleContent(ClaimAction[] Claims, User[] Members, ta_UserRole[] Assignments);
   }

   /// <summary>
   /// Satu module di daftar hak: namanya, seluruh hak yang dideklarasikannya, dan bagian dari hak
   /// itu yang sedang dipegang role yang terbuka.
   /// </summary>
   public class ClaimGroupVm : NotifyPropertyBase
   {
      private readonly Action<ClaimItemVm> _onClaimChanged;

      internal ClaimGroupVm(
         string moduleName,
         IEnumerable<ClaimAction> claims,
         IReadOnlyDictionary<string, int> usage,
         Action<ClaimItemVm> onClaimChanged) {

         _onClaimChanged = onClaimChanged;
         ModuleName = string.IsNullOrWhiteSpace(moduleName) ? "(no module)" : moduleName;

         foreach (var claim in claims) {
            Claims.Add(new ClaimItemVm(claim, usage.GetValueOrDefault(claim.Key), OnClaimChanged));
         }

         IsExpanded = true;
         IsCatalogueExpanded = true;
      }

      /// <summary>Nama module yang mendeklarasikan hak-hak di dalam grup ini.</summary>
      public string ModuleName { get; }

      /// <summary>Seluruh hak yang dideklarasikan module ini - inilah yang digambar lembar katalog.</summary>
      public ObservableCollection<ClaimItemVm> Claims { get; } = [];

      /// <summary>
      /// Bagian dari <see cref="Claims"/> yang sedang dipegang role yang terbuka - inilah yang
      /// digambar tab Permissions, yang memang hanya bicara soal hak yang dibawa role ini.
      /// </summary>
      public ObservableCollection<ClaimItemVm> Granted { get; } = [];

      /// <summary>Banyaknya hak dari module ini yang dipegang role yang terbuka.</summary>
      public int GrantedCount => Granted.Count;

      /// <summary>Angka di lencana grup pada tab Permissions.</summary>
      public string GrantedCaption => $"{GrantedCount:N0}";

      /// <summary>Angka di lencana grup pada lembar katalog, mis. "3 of 4".</summary>
      public string CatalogueCaption => $"{GrantedCount:N0} of {Claims.Count:N0}";

      /// <summary>Terbuka atau terlipat di tab Permissions.</summary>
      public bool IsExpanded {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// Terbuka atau terlipat di lembar katalog. Terpisah dari <see cref="IsExpanded"/> karena
      /// keduanya punya tombol "buka semua"-nya sendiri, dan satu module yang dilipat di satu daftar
      /// belum tentu ingin dilipat juga di daftar satunya.
      /// </summary>
      public bool IsCatalogueExpanded {
         get => Get<bool>();
         set => Set(value);
      }

      private void OnClaimChanged(ClaimItemVm claim) {
         RefreshGranted();
         _onClaimChanged(claim);
      }

      internal void RefreshGranted() {
         var wanted = Claims.Where(c => c.IsGranted).ToArray();

         for (var i = Granted.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(Granted[i])) Granted.RemoveAt(i);
         }

         for (var i = 0; i < wanted.Length; i++) {
            if (i < Granted.Count && ReferenceEquals(Granted[i], wanted[i])) continue;
            Granted.Insert(i, wanted[i]);
         }

         NotifyChanged(nameof(GrantedCount));
         NotifyChanged(nameof(GrantedCaption));
         NotifyChanged(nameof(CatalogueCaption));
      }
   }

   /// <summary>
   /// Satu hak di daftar: deklarasinya, berapa role yang membawanya, dan apakah role yang terbuka
   /// <i>ingin</i> membawanya - bukan apakah ia sudah membawanya menurut server.
   /// </summary>
   public class ClaimItemVm : NotifyPropertyBase
   {
      private readonly Action<ClaimItemVm> _onChanged;

      internal ClaimItemVm(ClaimAction action, int roleCount, Action<ClaimItemVm> onChanged) {
         _onChanged = onChanged;
         Action = action;
         RoleCount = roleCount;
      }

      /// <summary>Deklarasi hak ini - module pemiliknya dan namanya.</summary>
      public ClaimAction Action { get; }

      /// <summary>Kunci gabungan hak ini, <c>module:name</c>.</summary>
      public string Key => Action.Key;

      /// <summary>Nama hak ini tanpa nama module-nya, mis. <c>Approve</c>.</summary>
      public string Name => Action.Name;

      /// <summary>Banyaknya role yang membawa hak ini, di seluruh server.</summary>
      public int RoleCount { get; }

      /// <summary>Keterangan pemakaian hak ini untuk lembar katalog, mis. "Carried by 4 roles".</summary>
      public string UsageCaption => RoleCount == 1 ? "Carried by 1 role" : $"Carried by {RoleCount:N0} roles";

      /// <summary>
      /// Apakah role yang terbuka membawa hak ini. Inilah keadaan yang diinginkan: mengubahnya tidak
      /// mengirim apa pun ke server, hanya menggeser selisih yang akan dikirim tombol simpan.
      /// </summary>
      public bool IsGranted {
         get => Get<bool>();
         set => Set(value, _ => _onChanged(this));
      }
   }

   /// <summary>Keadaan satu penugasan role terhadap waktu server saat layar dimuat.</summary>
   public enum MemberPeriodState
   {
      /// <summary>Sedang berlaku.</summary>
      Active,

      /// <summary>Sudah diberikan, tapi tanggal mulainya belum tiba.</summary>
      Scheduled,

      /// <summary>Masa berlakunya sudah lewat.</summary>
      Expired
   }

   /// <summary>
   /// Satu baris di tab Members: siapa yang memegang role ini, sejak dan sampai kapan, dan apakah
   /// penugasan itu sedang berlaku.
   /// </summary>
   public class MemberRowVm : NotifyPropertyBase
   {
      private DateTime _now;

      internal MemberRowVm(User user, ta_UserRole? assignment, DateTime now) {
         User = user;
         _now = now;
         Start = assignment?.cUserRoleStart;
         Expiry = assignment?.cUserRoleExpiry;
      }

      /// <summary>User yang memegang role ini.</summary>
      public User User { get; }

      /// <summary>Nama akunnya.</summary>
      public string Account => User.cUserAccount;

      /// <summary>Nama lengkap orangnya.</summary>
      public string FullName => User.cContactFullName;

      /// <summary>Awal masa berlaku penugasan ini; <c>null</c> berarti sejak penugasannya dibuat.</summary>
      public DateTime? Start {
         get => Get<DateTime?>();
         set => Set(value, _ => RaiseDerived());
      }

      /// <summary>Akhir masa berlaku penugasan ini; <c>null</c> berarti tanpa akhir.</summary>
      public DateTime? Expiry {
         get => Get<DateTime?>();
         set => Set(value, _ => RaiseDerived());
      }

      /// <summary>Keterangan awal masa berlaku untuk kolom ASSIGNED.</summary>
      public string StartCaption => Start?.ToString("dd MMM yyyy") ?? "From the start";

      /// <summary>Keterangan akhir masa berlaku untuk kolom EXPIRES.</summary>
      public string ExpiryCaption => Expiry?.ToString("dd MMM yyyy") ?? "No end date";

      /// <summary>Keadaan penugasan ini terhadap waktu server saat layar dimuat.</summary>
      public MemberPeriodState State {
         get {
            if (Start is { } start && start > _now) return MemberPeriodState.Scheduled;
            if (Expiry is { } expiry && expiry < _now) return MemberPeriodState.Expired;
            return MemberPeriodState.Active;
         }
      }

      /// <summary>Keadaan penugasan ini sebagai teks untuk chip di kolom STATE.</summary>
      public string StateCaption => State switch {
         MemberPeriodState.Scheduled => "SCHEDULED",
         MemberPeriodState.Expired => "EXPIRED",
         _ => "ACTIVE"
      };

      // Dipanggil sesudah penyimpanan berhasil: baris ini sekarang mencerminkan apa yang tersimpan,
      // dan waktu acuannya ikut maju ke waktu server yang barusan dibaca.
      internal void Accept(ta_UserRole assignment, DateTime now) {
         _now = now;
         Start = assignment.cUserRoleStart;
         Expiry = assignment.cUserRoleExpiry;
         RaiseDerived();
      }

      private void RaiseDerived() {
         NotifyChanged(nameof(StartCaption));
         NotifyChanged(nameof(ExpiryCaption));
         NotifyChanged(nameof(State));
         NotifyChanged(nameof(StateCaption));
      }
   }
}
